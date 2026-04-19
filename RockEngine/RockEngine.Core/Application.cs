using NLog;

using RockEngine.Core.Coroutines;
using RockEngine.Core.DI;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.ECS;
using RockEngine.Core.Physics;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace RockEngine.Core
{
    public abstract class Application : IDisposable
    {
        private IApplicationContext _context;
        private readonly Scope _applicationScope;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Core components
        protected IWindow _window;
        protected VulkanContext _vulkanContext;
        protected GraphicsContext _graphicsContext;
        protected WorldRenderer _renderer;
        protected World _world;
        protected CoroutineScheduler _coroutineScheduler;
        protected PhysicsManager _physicsManager;

        private readonly CancellationTokenSource _appCts = new();
        private readonly TaskCompletionSource _initializedTcs = new();
        private bool _isInitialized;

        protected abstract Type GetContextType();

        protected Application()
        {
            IoC.Initialize(this);
            _applicationScope = AsyncScopedLifestyle.BeginScope(IoC.Container);
            ConfigureWindow();
        }


        private void ConfigureWindow()
        {
            var settings = IoC.Container.GetInstance<AppSettings>();

            _window = IoC.Container.GetInstance<IWindow>();

            // Setup event handlers
            _window.Load += () =>
            {
                OnWindowLoad().GetAwaiter().GetResult();
            };
            _window.Update += (delta) => OnWindowUpdate(delta).GetAwaiter().GetResult();
            _window.Render += (delta) => OnWindowRender(delta).GetAwaiter().GetResult();
            _window.Initialize();
            _window.StateChanged += _window_StateChanged;
        }

        private void _window_StateChanged(WindowState obj)
        {

        }

        private async Task OnWindowLoad()
        {
            try
            {
                _logger.Info("Initializing core systems...");

                _vulkanContext = IoC.Container.GetInstance<VulkanContext>();
                _graphicsContext = IoC.Container.GetInstance<GraphicsContext>();
                _coroutineScheduler = IoC.Container.GetInstance<CoroutineScheduler>();
                PerformanceTracer.Initialize(_vulkanContext);

                var surface = SurfaceHandler.CreateSurface(_window, _vulkanContext);
                var swapchain = VkSwapchain.Create(_vulkanContext, surface);
                _graphicsContext.AddSwapchain(swapchain);

                _renderer = IoC.Container.GetInstance<WorldRenderer>();
                _world = IoC.Container.GetInstance<World>();
                _physicsManager = IoC.Container.GetInstance<PhysicsManager>();

                var shaderManager = IoC.Container.GetInstance<IShaderManager>();
                await shaderManager.CompileAllShadersAsync();

                await _renderer.InitializeAsync();
                await _world.Start(_renderer);
                _physicsManager.Initialize();

                // Resolve context after container is fully ready
                _context = (IApplicationContext)IoC.Container.GetInstance(GetContextType());
                await _context.InitializeAsync(_graphicsContext, _renderer, _world);

                _isInitialized = true;
                _initializedTcs.SetResult();
                _logger.Info("Application initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Initialization failed.");
                _initializedTcs.SetException(ex);
                _window.Close();
            }
        }


        private async Task OnWindowUpdate(double delta)
        {
            if (!_isInitialized || _appCts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                Time.Update(_window.Time);

                // Let context do its own update logic
                await _context.UpdateAsync();

           
                _coroutineScheduler.Update();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Update failed.");
            }
        }

        private async Task OnWindowRender(double delta)
        {
            if (!_isInitialized || _appCts.IsCancellationRequested)
            {
                return;
            }

            PerformanceTracer.ProcessQueries(_vulkanContext, _graphicsContext.FrameIndex);
            PerformanceTracer.BeginFrame(_graphicsContext.FrameIndex);

            _graphicsContext.BeginFrame();

            try
            {
                var renderContext = new RenderContext(
                    _graphicsContext.FrameIndex,
                    _vulkanContext.GraphicsSubmitContext,
                    _vulkanContext.TransferSubmitContext,
                    _vulkanContext.ComputeSubmitContext,
                    _renderer);

                // Delegate to context for rendering
                await _context.RenderAsync(renderContext);

                _graphicsContext.SubmitAndPresent();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Render failed.");
            }
        }


       /* private void RenderImGui(RenderContext renderContext)
        {
            var batch = _vulkanContext.GraphicsSubmitContext.CreateBatch();
            using (PerformanceTracer.BeginSection("ImGui Render"))
            {
                using (batch.BeginSection("ImGui", _graphicsEngine.FrameIndex))
                {
                    _layerStack.RenderImGui(batch);
                }
            }
            batch.Submit();
        }

        private void RenderLayers(RenderContext renderContext)
        {
            var batch = _vulkanContext.GraphicsSubmitContext.CreateBatch();
            using (PerformanceTracer.BeginSection("Layer Render"))
            {
                using (batch.BeginSection("Layers", _graphicsEngine.FrameIndex))
                {
                    _layerStack.Render(batch);
                }
            }
            batch.Submit();
        }*/

      /*  private async Task RenderWorld(RenderContext renderContext)
        {
            using (PerformanceTracer.BeginSection("World Render"))
            {
                await _renderer.Render(renderContext);
            }
        }*/

        public void Run()
        {
            try
            {
                _window.Run();
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Application crashed");
                throw;
            }
        }
        public void Stop()
        {
            _window?.Close();
        }

        public virtual void Dispose()
        {
            if (_appCts.IsCancellationRequested)
            {
                return;
            }

            _appCts.Cancel();

            try
            {
                _initializedTcs.Task.Wait(TimeSpan.FromSeconds(5));
                _logger.Info("Shutting down...");

                _context.ShutdownAsync().GetAwaiter().GetResult();
                _vulkanContext?.Device?.WaitIdle();

                _world?.Dispose();
                _renderer?.Dispose();
                _graphicsContext?.Dispose();
                _vulkanContext?.Dispose();
                _applicationScope?.Dispose();

                _logger.Info("Shutdown complete.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during shutdown.");
            }
            finally
            {
                _appCts.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}