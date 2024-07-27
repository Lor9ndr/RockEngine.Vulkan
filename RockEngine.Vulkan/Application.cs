using ImGuiNET;

using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.ImGuiRender;
using RockEngine.Vulkan.Utils;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Input;
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

            _window.Load += async () => await InitializeAsync().ConfigureAwait(false);
            _window.Update += Update;

            try
            {
                await Task.Run(() => _window.Run(), CancellationToken).ConfigureAwait(false);
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

        private async Task InitializeAsync()
        {
            _inputContext = _window.CreateInput();
            _context = new VulkanContext(_window, "Lor9ndr");
            _baseRenderer = new BaseRenderer(_context, _context.Surface);
            _assetManager = new AssetManager();

            await LoadProjectAsync();
            await InitializeRenderSystemsAsync();
            _window.Render += async (s) => await DrawFrame(s).ConfigureAwait(false);

            await InitializeSceneAsync();
        }

        private async Task LoadProjectAsync()
        {
            _project = await _assetManager.CreateProjectAsync("Sandbox", "F:\\RockEngine.Vulkan\\RockEngine.Vulkan\\bin\\Debug\\net8.0\\Sandbox.asset", CancellationToken);
            var scene = _project.Scenes[0];
            await _assetManager.SaveAssetAsync(scene, CancellationToken);
        }

        private async Task InitializeRenderSystemsAsync()
        {
            _sceneRenderSystem = new SceneRenderSystem(_context, _baseRenderer.GetRenderPass());
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
        }

        private async Task InitializeSceneAsync()
        {
            var scene = _project.Scenes[0];
            var camera = new Entity();
            await camera.AddComponent(_context, new DebugCamera(_inputContext, MathHelper.DegreesToRadians(90), _window.Size.X / _window.Size.Y, 0.1f, 1000, camera));
            await scene.AddEntity(_context, camera);

            for (int i = 0; i < 1; i++)
            {
                var entity = new Entity();
                await entity.AddComponent(_context, new MeshComponent(entity, DefaultMesh.CubeVertices));
                await entity.AddComponent(_context, new Material(entity, await Texture.FromFileAsync(_context, "C:\\Users\\Админис\\Desktop\\texture.jpg", CancellationToken)));
                entity.Transform.Scale = new Vector3(4, 4, 4);
                entity.Transform.Position = new Vector3(0, 0, -5);
                await scene.AddEntity(_context, entity);
            }

            await scene.InitializeAsync(_context);
        }

        private async Task DrawFrame(double obj)
        {
            var commandBuffer = await _baseRenderer.BeginFrameAsync().ConfigureAwait(false);
            if (commandBuffer == null)
            {
                return;
            }

            _imguiController.Update((float)obj);
            ImGui.ShowDemoWindow();

            _baseRenderer.BeginSwapchainRenderPass(in commandBuffer);
            await _sceneRenderSystem.RenderAsync(_project, commandBuffer, _baseRenderer.FrameIndex).ConfigureAwait(false);
            _imguiController.RenderAsync(commandBuffer, _baseRenderer.Swapchain.Extent);
            await _imguiController.RenderWindows().ConfigureAwait(false);
            _baseRenderer.EndSwapchainRenderPass(in commandBuffer);
            _baseRenderer.EndFrame();
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
