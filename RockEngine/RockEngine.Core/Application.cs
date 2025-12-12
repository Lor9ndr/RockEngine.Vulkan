using NLog;

using RockEngine.Core.Coroutines;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.Extensions;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

using System.Diagnostics;

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
            _window.Closing += OnWindowClosing;
            _window.Update +=  (delta) =>  OnWindowUpdate(delta).GetAwaiter().GetResult();
            _window.Render += OnWindowRender;
            _window.Resize += OnWindowResize;
            _window.StateChanged += OnWindowStateChanged;
            _window.FocusChanged += OnWindowFocusChanged;
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
                var surface = SurfaceHandler.CreateSurface(_window, _context);
                
                _graphicsEngine.AddSwapchain(VkSwapchain.Create(_context, surface));
                _renderer = IoC.Container.GetInstance<WorldRenderer>();
                _layerStack = IoC.Container.GetInstance<LayerStack>();
                _world = IoC.Container.GetInstance<World>();

                // Initialize shaders
                var shaderManager = IoC.Container.GetInstance<IShaderManager>();
                await shaderManager.CompileAllShadersAsync();

                // Initialize renderer
                await _renderer.InitializeAsync();
                await _world.Start(_renderer);

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

        private async Task OnWindowUpdate(double deltaTime)
        {
            if (!_isInitialized || _appCts.IsCancellationRequested)
                return;

            try
            {
                // Update time system
                Time.Update(_window.Time, deltaTime);

                // Update layers
                _layerStack.Update();

                // Update world
                await _world.Update(_renderer);

                // Update renderer frame data
                await _renderer.UpdateFrameData();
                _coroutineSheduler.Update();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Update failed");
            }

        }

      

        private void OnWindowRender(double deltaTime)
        {
            if (!_isInitialized || _isMinimized || _appCts.IsCancellationRequested)
                return;



            PerformanceTracer.ProcessQueries(_context, _graphicsEngine.FrameIndex);
            PerformanceTracer.BeginFrame(_graphicsEngine.FrameIndex);

            // Begin frame
            var batch = _graphicsEngine.BeginFrame();

            try
            {
                // Render ImGui
                RenderImGui(batch);

                // Render layers
                RenderLayers(batch);

                // Render world
                RenderWorld();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Render failed");
                HandleRenderFailure(ex);
            }

            // Submit and present
            var presented = _graphicsEngine.SubmitAndPresent();
            if (!presented)
            {

            }



        }
        private void HandleRenderFailure(Exception ex)
        {
            // Wait a bit before retrying
            Thread.Sleep(16);
        }

      


        private void RenderImGui(UploadBatch batch)
        {
            using (PerformanceTracer.BeginSection("ImGui Render"))
            using (batch.BeginSection("ImGui", _graphicsEngine.FrameIndex))
            {
                _layerStack.RenderImGui(batch);
            }
        }

        private void RenderLayers(UploadBatch batch)
        {
            using (PerformanceTracer.BeginSection("Layer Render"))
            using (batch.BeginSection("Layers", _graphicsEngine.FrameIndex))
            {
                var renderBatch = _context.GraphicsSubmitContext.CreateBatch();
                _layerStack.Render(renderBatch);
                renderBatch.Submit();
            }
        }

        private  void RenderWorld()
        {
            using (PerformanceTracer.BeginSection("World Render"))
            {
                _renderer.Render().GetAwaiter().GetResult();
            }
        }

       

        private void OnWindowResize(Silk.NET.Maths.Vector2D<int> size)
        {
            if (!_isInitialized || size.X <= 0 || size.Y <= 0)
            {
                while(size.X <= 0 || size.Y <= 0)
                {
                    _window.DoEvents();
                }
                return;
            }


            // Wait a bit before resizing to avoid rapid resize events
            Thread.Sleep(10);
        }

        private void OnWindowStateChanged(WindowState state)
        {
            _isMinimized = (state == WindowState.Minimized);

            if (_isMinimized)
            {
                // Pause rendering when minimized
                _context.Device.WaitIdle();
            }
        }

        private void OnWindowFocusChanged(bool isFocused)
        {
            // Handle focus changes if needed
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
                Dispose();
            }
        }
        public void Stop()
        {
            _window?.Close();
        }

        protected virtual Task Load() => Task.CompletedTask;

        private void OnWindowClosing()
        {
            Dispose();
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

                // Wait for device idle
                _context?.Device?.WaitIdle();

                // Dispose components in reverse order
                _world?.Dispose();
                _layerStack?.Dispose();
                _renderer?.Dispose();
                _graphicsEngine?.Dispose();
                _context?.Dispose();

                // Close window
                _window?.Close();
                _window?.Dispose();

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

        /// <summary>
        /// Execute an action on the main thread (window thread)
        /// </summary>
        public void ExecuteOnMainThread(Action action)
        {
            if (_window == null)
                return;

            // Silk.NET windows run on the main thread, so we can execute directly
            // For async operations, we need to use the window's thread context
            if (Environment.CurrentManagedThreadId == Environment.CurrentManagedThreadId)
            {
                action();
            }
            else
            {
                // Queue for execution on next update
                _window.Invoke(action);
            }
        }

        /// <summary>
        /// Execute a function on the main thread (window thread)
        /// </summary>
        public T ExecuteOnMainThread<T>(Func<T> func)
        {
            if (_window == null)
                return default;

            if (Environment.CurrentManagedThreadId == Environment.CurrentManagedThreadId)
            {
                return func();
            }
            else
            {
                T result = default;
                _window.Invoke(() => result = func());
                return result;
            }
        }

        /// <summary>
        /// Wait for application initialization
        /// </summary>
        public async Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
        {
            await Task.Run(() => _initialized.Wait(cancellationToken));
        }

    }
}