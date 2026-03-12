using NLog;

using RockEngine.Core.Coroutines;
using RockEngine.Core.DI;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.ECS;
using RockEngine.Core.Extensions;
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
        private readonly Scope _applicationScope;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Core components
        protected IWindow _window;
        protected VulkanContext _context;
        protected GraphicsContext _graphicsEngine;
        private CoroutineScheduler _coroutineSheduler;
        protected WorldRenderer _renderer;
        protected LayerStack _layerStack;
        protected World _world;
        private PhysicsManager _physicsManager;

        // Synchronization
        private readonly CancellationTokenSource _appCts = new();
        private readonly ManualResetEventSlim _initialized = new(false);
        private bool _isInitialized;
        private bool _isMinimized;
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
            _window.Load +=  () =>
            {
                 OnWindowLoad().GetAwaiter().GetResult();
            };
            _window.Update +=  (delta) =>  OnWindowUpdate(delta).GetAwaiter().GetResult();
            _window.Render += (delta) => OnWindowRender(delta).GetAwaiter().GetResult();
            _window.Initialize();
        }

        

        private async Task OnWindowLoad()
        {
            try
            {
                _logger.Info("Initializing application...");

                // Initialize on window thread (required for Vulkan)
                _context = IoC.Container.GetInstance<VulkanContext>();
                _graphicsEngine = IoC.Container.GetInstance<GraphicsContext>();
                _coroutineSheduler = IoC.Container.GetInstance<CoroutineScheduler>();
                PerformanceTracer.Initialize(_context);
                var surface = SurfaceHandler.CreateSurface(_window, _context);
                
                _graphicsEngine.AddSwapchain(VkSwapchain.Create(_context, surface));
                _renderer = IoC.Container.GetInstance<WorldRenderer>();
                _layerStack = IoC.Container.GetInstance<LayerStack>();
                _world = IoC.Container.GetInstance<World>();
                _physicsManager = IoC.Container.GetInstance<PhysicsManager>();

                // Initialize shaders
                var shaderManager = IoC.Container.GetInstance<IShaderManager>();
                await shaderManager.CompileAllShadersAsync();

                // Initialize renderer
                await _renderer.InitializeAsync();
                await _world.Start(_renderer);
                _physicsManager.Initialize();

                // Load application content
                await Load();

                _isInitialized = true;
                _initialized.Set();

                _logger.Info("Application initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize application");
                _window.Close();
                throw;
            }
        }

        private async Task OnWindowUpdate(double _)
        {
            if (!_isInitialized || _appCts.IsCancellationRequested)
                return;

            try
            {
                // Update time system
                Time.Update(_window.Time);

                // Update layers
                _layerStack.Update();

                // Update world
                await _world.Update(_renderer);
                _physicsManager.Update(Time.DeltaTime);


                // Update renderer frame data
                await _renderer.UpdateFrameData();
                _coroutineSheduler.Update();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Update failed");
            }

        }

        private async Task OnWindowRender(double deltaTime)
        {
            if (!_isInitialized  || _appCts.IsCancellationRequested)
                return;

            PerformanceTracer.ProcessQueries(_context, _graphicsEngine.FrameIndex);

            PerformanceTracer.BeginFrame(_graphicsEngine.FrameIndex);

            // Begin frame
            _graphicsEngine.BeginFrame();
            
            try
            {
                RenderContext renderContext = new RenderContext(
                    _graphicsEngine.FrameIndex,
                    _context.GraphicsSubmitContext,
                    _context.TransferSubmitContext,
                    _context.ComputeSubmitContext,
                    _renderer);

                // Render ImGui
                RenderImGui(renderContext);

                // Render layers
                RenderLayers(renderContext);

                // Render world
                await RenderWorld(renderContext);

                // Submit and present

                _graphicsEngine.SubmitAndPresent();

            }
            catch (VulkanException ex) 
            {
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Render failed");
            }

           
        }
       

        private void RenderImGui(RenderContext renderContext)
        {
            var batch = _context.GraphicsSubmitContext.CreateBatch();
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
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            using (PerformanceTracer.BeginSection("Layer Render"))
            {
                using (batch.BeginSection("Layers", _graphicsEngine.FrameIndex))
                {
                    _layerStack.Render(batch);
                }
            }
            batch.Submit();
        }

        private async Task RenderWorld(RenderContext renderContext)
        {
            using (PerformanceTracer.BeginSection("World Render"))
            {
               await _renderer.Render(renderContext);
            }
        }

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
            finally
            {
                // GC will correctly collect the vk objects and dispose them since they are disposable
                //Dispose();
            }
        }
        public void Stop()
        {
            _window?.Close();
        }

        protected virtual async Task Load()
        {
            var layers = IoC.Container.GetAllInstances<ILayer>();
            foreach (var item in layers)
            {
                await _layerStack.PushLayer(item);
            }
        }


        public virtual void Dispose()
        {
            if (_appCts.IsCancellationRequested)
                return;

            _appCts.Cancel();

            try
            {
                _initialized.Wait(TimeSpan.FromSeconds(5));

                _logger.Info("Shutting down application...");
                _context?.Device?.WaitIdle();
                _world?.Dispose();
                _layerStack?.Dispose();
                _renderer?.Dispose();
                _graphicsEngine?.Dispose();
                _context?.Dispose();
               
                _applicationScope?.Dispose();

                _logger.Info("Application shutdown complete");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during shutdown");
            }
            finally
            {
                _appCts.Dispose();
                _initialized.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}