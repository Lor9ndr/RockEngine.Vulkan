using NLog;
using NLog.Targets;
using RockEngine.Core.Coroutines;
using RockEngine.Core.DI;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.ECS;
using RockEngine.Core.Physics;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Synchronization;
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

        private MainThreadSynchronizationContext? _mainSyncCtx;
        protected abstract Type GetContextType();

        protected Application()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var consoleTarget = new ConsoleTarget
            {
                Layout = "${time}|${level:uppercase=true}|${logger}|${message}${onexception:${newline}${exception:format=tostring:maxInnerExceptionLevel=10}}"
            };
            config.AddTarget("EditorConsole", consoleTarget);
            config.AddRuleForAllLevels(consoleTarget);
            LogManager.Configuration = config;

            IoC.Initialize(this);
            _applicationScope = AsyncScopedLifestyle.BeginScope(IoC.Container);
            ConfigureWindow();
        }


        private void ConfigureWindow()
        {
            var settings = IoC.Container.GetInstance<AppSettings>();

            _window = IoC.Container.GetInstance<IWindow>();


            // Setup event handlers
            // In ConfigureWindow()
            _mainSyncCtx = MainThreadSynchronizationContext.Install();

            _window.Load += () =>
            {
                MainThreadSynchronizationContext.WaitOnMainThread(OnWindowLoad());
                _mainSyncCtx?.ProcessAllQueuedWork(); 
            };

            // Update – stays synchronous, but pumps the queue while waiting
            _window.Update += (delta) =>
            {
                MainThreadSynchronizationContext.WaitOnMainThread(OnWindowUpdate(delta));
                _mainSyncCtx?.ProcessAllQueuedWork(); // drain any pending work from other threads

            };

            // Render – also synchronous with pumping
            _window.Render += OnWindowRender;
            _window.Initialize();
           
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
                await shaderManager.CompileAllShadersAsync().ConfigureAwait(true);

                await _renderer.InitializeAsync().ConfigureAwait(true);
                await _world.Start(_renderer).ConfigureAwait(true);
                _physicsManager.Initialize();

                // Resolve context after container is fully ready
                _context = (IApplicationContext)IoC.Container.GetInstance(GetContextType());
                await _context.InitializeAsync(_graphicsContext, _renderer, _world).ConfigureAwait(true);

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


                await _context.UpdateAsync().ConfigureAwait(true);
                _mainSyncCtx?.ProcessUpdateWork();

                _coroutineScheduler.Update();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Update failed.");
            }
        }

        private void OnWindowRender(double delta)
        {
            if (!_isInitialized || _appCts.IsCancellationRequested )
            {
                return;
            }

            PerformanceTracer.ProcessQueries(_vulkanContext, _graphicsContext.FrameIndex);
            PerformanceTracer.BeginFrame(_graphicsContext.FrameIndex);

            if (_graphicsContext.BeginFrame() is null)
            {
                //_graphicsContext.SubmitAndPresent();
                return;
            }


            try
            {
                var renderContext = new RenderContext(
                    _graphicsContext.FrameIndex,
                    _vulkanContext.GraphicsSubmitContext,
                    _vulkanContext.TransferSubmitContext,
                    _vulkanContext.ComputeSubmitContext,
                    _renderer);

                // Delegate to context for rendering
                MainThreadSynchronizationContext.WaitOnMainThread(_context.RenderAsync(renderContext));

                // All work scheduled with RunOnRender() will be executed here
                _mainSyncCtx?.ProcessRenderWork();
                _mainSyncCtx?.ProcessAllQueuedWork();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Render failed.");
            }
            finally
            {
                try
                {
                    _graphicsContext.SubmitAndPresent();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Render failed.");
                }
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