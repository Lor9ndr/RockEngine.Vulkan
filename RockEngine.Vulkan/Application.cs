using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Utils;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Input;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

using System.Numerics;

namespace RockEngine.Vulkan
{
    public class Application : IDisposable
    {
        private readonly Vk _api = Vk.GetApi();
        private IWindow _window;
        private VulkanContext _context;
        private Project _project;
        private AssetManager _assetManager;
        private IInputContext _inputContext;
        private GlfwSurfaceHandler _surface;
        private BaseRenderer _baseRenderer;

        public CancellationTokenSource CancellationTokenSource { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Application()
        {
            CancellationTokenSource = new CancellationTokenSource();
            CancellationToken = CancellationTokenSource.Token;
        }

        public async Task RunAsync()
        {
            IoC.Register();
            _window = Window.Create(WindowOptions.DefaultVulkan);
            _window.Title = "RockEngine";
            _window.Load += async () =>
            {
                _inputContext = _window.CreateInput();
                try
                {
                    await Window_Load().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    throw;
                }
            };
            _window.Closing += Dispose;

            try
            {
                await Task.Run(() => _window.Run(), CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation
            }
            catch (Exception)
            {
                throw;
            }

        }
      

        private void DrawFrame(double obj)
        {
            _baseRenderer.Render(obj, _project);
        }

        private async Task Window_Load()
        {
            _context = new VulkanContext(_window, "Lor9ndr");
            _assetManager = new AssetManager();

            _project = await _assetManager.CreateProjectAsync("Sandbox", "F:\\RockEngine.Vulkan\\RockEngine.Vulkan\\bin\\Debug\\net8.0\\Sandbox.asset", CancellationToken);
            var scene = _project.Scenes[0];
            var savingTask =  _assetManager.SaveAssetAsync(scene, CancellationToken);
            _surface = GlfwSurfaceHandler.CreateSurface(_window, _context);
            
            _baseRenderer = new BaseRenderer(_context, _surface);
            await _baseRenderer.InitializeAsync().ConfigureAwait(false);
            _window.Update += Update;
            _window.Render += DrawFrame;

            await savingTask;
            var camera = new Entity();
            await camera.AddComponent(_context,
                new DebugCamera(_inputContext, MathHelper.DegreesToRadians(90), _window.Size.X / _window.Size.Y, 0.1f, 1000,camera));
            await scene.AddEntity(context: _context, camera);
            var debug = camera.GetComponent<DebugCamera>();

            var entity = new Entity();
            await entity.AddComponent(_context, new MeshComponent(entity, DefaultMesh.CubeVertices));
            entity.Transform.Scale = new Vector3(10,10,10);
            await scene.AddEntity(_context, entity);

            await scene.InitializeAsync(_context);
        }

        private void Update(double time)
        {
           _project.CurrentScene.Update(time);
        }

        public void Dispose()
        {
            CancellationTokenSource.Cancel();
            _api.DeviceWaitIdle(_context.Device);

            _baseRenderer.Dispose();
            IoC.Container.Dispose();

            _project.Dispose();
            _inputContext.Dispose();
            _context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}