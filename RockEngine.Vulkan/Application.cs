using ImGuiNET;

using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.GUI;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.ImGuiRender;
using RockEngine.Vulkan.Utils;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Input;
using Silk.NET.SDL;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

using System.Numerics;

using Texture = RockEngine.Vulkan.VkObjects.Texture;
using Window = Silk.NET.Windowing.Window;

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
        private BaseRenderer _baseRenderer;
        private SceneRenderSystem _sceneRenderSystem;
        private ImGuiController _imguiController;

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
            SdlWindowing.Use();
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
            //_window.Closing += Dispose;

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
      

        private async Task DrawFrame(double obj)
        {

            var commandBuffer = _baseRenderer.BeginFrame();
            if (commandBuffer == null)
            {
                return;
            }


            _imguiController.Update((float)obj);
            ImGui.ShowDemoWindow();

            _baseRenderer.BeginSwapchainRenderPass(commandBuffer);

            await _sceneRenderSystem.RenderAsync(_project, commandBuffer, _baseRenderer.FrameIndex);

            _imguiController.Render(commandBuffer, _baseRenderer.Swapchain.Extent);

            _baseRenderer.EndSwapchainRenderPass(commandBuffer);
            _baseRenderer.EndFrame();


        }

        private async Task Window_Load()
        {
            _context = new VulkanContext(_window, "Lor9ndr");
            _baseRenderer = new BaseRenderer(_context,_context.Surface);

            _assetManager = new AssetManager();

            _project = await _assetManager.CreateProjectAsync("Sandbox", "F:\\RockEngine.Vulkan\\RockEngine.Vulkan\\bin\\Debug\\net8.0\\Sandbox.asset", CancellationToken);
            var scene = _project.Scenes[0];
            var savingTask =  _assetManager.SaveAssetAsync(scene, CancellationToken);
            
            _sceneRenderSystem = new SceneRenderSystem(_context,_baseRenderer.GetRenderPass());
            await _sceneRenderSystem.Init(CancellationToken).ConfigureAwait(false);
            _imguiController = new ImGuiController(
                _context,
                _window, 
                _inputContext,
                _context.Device.PhysicalDevice,
                _context.Device.QueueFamilyIndices.GraphicsFamily.Value,
                _baseRenderer.Swapchain.Images.Length, 
                _baseRenderer.Swapchain.Format,
                _baseRenderer.Swapchain.DepthFormat, 
                _baseRenderer.GetRenderPass());


            _window.Update += Update;
            _window.Render += async (s) => await DrawFrame(s);
            await savingTask;
            var camera = new Entity();
            await camera.AddComponent(_context,
                new DebugCamera(_inputContext, MathHelper.DegreesToRadians(90), _window.Size.X / _window.Size.Y, 0.1f, 1000,camera));
            await scene.AddEntity(context: _context, camera);
            var debug = camera.GetComponent<DebugCamera>();

            var entity = new Entity();
            await entity.AddComponent(_context, new MeshComponent(entity, DefaultMesh.CubeVertices));
            await entity.AddComponent(_context, new Material(entity, await Texture.FromFileAsync(_context, "C:\\Users\\Админис\\Desktop\\texture.jpg",CancellationToken)));
            entity.Transform.Scale = new Vector3(5,5,5);
            entity.Transform.Position = new Vector3(0,0,-10);
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
            _sceneRenderSystem.Dispose();
            _baseRenderer.Dispose();
            IoC.Container.Dispose();

            _project.Dispose();
            _inputContext.Dispose();
            _context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}