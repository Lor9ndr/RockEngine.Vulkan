using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.Coroutines;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

using System.Diagnostics;

namespace RockEngine.Core
{
    public abstract class Application : IDisposable
    {
        protected VulkanContext _context;
        protected IWindow _window;
        protected LayerStack _layerStack;
        protected GraphicsContext _graphicsEngine;
        protected IInputContext _inputContext;
        protected WorldRenderer _renderer;
        protected World _world;
        protected PipelineManager _pipelineManager;
        protected AssimpLoader _assimpLoader;
        protected readonly AppSettings _appSettings;

        private IShaderManager _shaderManager;
        private Task[] _renderTasks;
        private CoroutineScheduler _coroutineScheduler;
        private AssetManager _assetManager;
        private readonly Container _container;
        private readonly Scope _applicationScope;

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken CancellationToken => CancellationTokenSource.Token;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();



        public Application()
        {
            // Initialize DI container if not already initialized
            if (!IoC.Container.IsLocked)
            {
                IoC.Initialize();
            }
            _applicationScope = AsyncScopedLifestyle.BeginScope(IoC.Container);

            _appSettings = IoC.Container.GetInstance<AppSettings>();
        }

        public void Run()
        {
            CancellationTokenSource = new CancellationTokenSource();
            // Configure window
            _window = IoC.Container.GetInstance<IWindow>();
            _window.Title = _appSettings.Name;
            _window.Size = _appSettings.LoadSize;
            _window.IsEventDriven = false;

            // Resolve other dependencies
            _world = IoC.Container.GetInstance<World>();
            _assimpLoader = IoC.Container.GetInstance<AssimpLoader>();
            _window.Load += () =>
            {
                try
                {
                    OnWindowLoad().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to load window.");
                    throw;
                }
            };

            _window.Run();
        }

        private async Task OnWindowLoad()
        {
            Stopwatch loadWatch = Stopwatch.StartNew();
            _inputContext = IoC.Container.GetInstance<IInputContext>();

            // Resolve window-dependent components
            _context = IoC.Container.GetInstance<VulkanContext>();
            _graphicsEngine = IoC.Container.GetInstance<GraphicsContext>();
            _pipelineManager = IoC.Container.GetInstance<PipelineManager>();
            _renderer = IoC.Container.GetInstance<WorldRenderer>();
            _shaderManager = IoC.Container.GetInstance<IShaderManager>();
            _renderTasks = new Task[2];
            _coroutineScheduler = IoC.Container.GetInstance<CoroutineScheduler>();
            _assetManager = IoC.Container.GetInstance<AssetManager>();


            await _shaderManager.CompileAllShadersAsync();

            PerformanceTracer.Initialize(_context);
            PerformanceTracer.ProcessQueries(_context, _graphicsEngine.FrameIndex);
            PerformanceTracer.BeginFrame(_graphicsEngine.FrameIndex);
            _assetManager.OnProjectLoaded += (_) =>
            {
                DefaultMeshes.Initalize(_assetManager);
            };
            await _renderer.InitializeAsync().ConfigureAwait(false);
            await _world.Start(_renderer).ConfigureAwait(false);

            _logger.Info($"Core systems initialized in: {loadWatch.ElapsedMilliseconds} ms");

            _layerStack = IoC.Container.GetInstance<LayerStack>();


            await Load().ConfigureAwait(false);
            _window.Update += (s) =>
            {
                try
                {
                    Update(s).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception in Update. Closing application. :{0}, \n {1}", ex.Message, ex.StackTrace);
                    throw;
                }
            };

            _window.Render += (s) =>
            {
                try
                {
                    Render(s).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception in Render. Closing application. :{0}, \n {1}", ex.Message, ex.StackTrace);
                    throw;
                }
            };

            loadWatch.Stop();
            _logger.Info($"Application loaded in: {loadWatch.ElapsedMilliseconds} ms");

        }

        protected virtual async Task Load()
        {
            foreach (var item in IoC.Container.GetAllInstances<ILayer>())
            {
                await _layerStack.PushLayer(item);
            }
        }

        protected virtual async Task Update(double deltaTime)
        {
            using (PerformanceTracer.BeginSection("Update"))
            {
                Time.Update(_window.Time, deltaTime);
                _layerStack.Update();
                _coroutineScheduler.Update();
                await _world.Update(_renderer).ConfigureAwait(false);
                await _renderer.UpdateFrameData().ConfigureAwait(false);
            }
        }

        protected virtual async Task Render(double time)
        {
            using (PerformanceTracer.BeginSection("Whole Render"))
            {
                if (_layerStack.Count == 0)
                {
                    return;
                }

                var batch = _graphicsEngine.Begin();
                if (batch is null)
                {
                    return;
                }

                var frameIndex = _renderer.FrameIndex;

                PerformanceTracer.ProcessQueries(_context, frameIndex);
                PerformanceTracer.BeginFrame(frameIndex);


                using (PerformanceTracer.BeginSection("_layerStack.RenderImGui"))
                {
                    using (PerformanceTracer.BeginSection("RenderImGui", batch.CommandBuffer, frameIndex))
                    {
                        await _layerStack.RenderImGui(batch.CommandBuffer);
                    }
                }


                _renderTasks[0] = Task.Run(() => {
                    using (PerformanceTracer.BeginSection("_layerStack.Render"))
                    {
                        var renderBatch = _context.GraphicsSubmitContext.CreateBatch();
                        using (PerformanceTracer.BeginSection("_layerStack.Render", renderBatch.CommandBuffer, frameIndex))
                        {
                            _layerStack.Render(renderBatch.CommandBuffer);
                        }
                        renderBatch.Submit();
                    }
                });

                _renderTasks[1] = Task.Run(async () =>
                {
                    using (PerformanceTracer.BeginSection("_renderer.Render()"))
                    {
                        await _renderer.Render().ConfigureAwait(false);
                    }
                });

                await Task.WhenAll(_renderTasks).ConfigureAwait(false);


                using (PerformanceTracer.BeginSection("_graphicsEngine.end & Submit"))
                {
                    _graphicsEngine.SubmitAndPresent(batch);
                }
            }
        }

        public Task PushLayer(ILayer layer) => _layerStack.PushLayer(layer);
        public void PopLayer(ILayer layer) => _layerStack.PopLayer(layer);


        public virtual void Dispose()
        {
            CancellationTokenSource?.Cancel();
            _context.Device.GraphicsQueue.WaitIdle();
            _context.Device.ComputeQueue.WaitIdle();

            _renderer?.Dispose();
            _graphicsEngine?.Dispose();
            _context?.Dispose();

            if (_window != null)
            {
                _window.Close();
                _window.Dispose();
            }

            _applicationScope?.Dispose();
            _container?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
/*
using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.Coroutines;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.TPL;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

using System.Diagnostics;

namespace RockEngine.Core
{
    public enum ApplicationState
    {
        Initializing,
        Loading,
        Running,
        ShuttingDown
    }

    public abstract class Application : IDisposable
    {
        protected VulkanContext _context;
        protected IWindow _window;
        protected LayerStack _layerStack;
        protected GraphicsContext _graphicsEngine;
        protected IInputContext _inputContext;
        private VulkanSynchronizationContext _vulkanSyncContext;
        protected WorldRenderer _renderer;
        protected World _world;
        protected PipelineManager _pipelineManager;
        protected AssimpLoader _assimpLoader;
        protected readonly AppSettings _appSettings;

        private IShaderManager _shaderManager;
        private CoroutineScheduler _coroutineScheduler;
        private AssetManager _assetManager;
        private readonly Container _container;
        private readonly Scope _applicationScope;

        // Application state management
        private ApplicationState _currentState = ApplicationState.Initializing;
        private readonly TaskCompletionSource<bool> _loadingCompleted = new TaskCompletionSource<bool>();
        private readonly CancellationTokenSource _applicationCts = new CancellationTokenSource();

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken CancellationToken => CancellationTokenSource.Token;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public ApplicationState CurrentState => _currentState;
        public Task LoadingTask => _loadingCompleted.Task;

        public Application()
        {
            if (!IoC.Container.IsLocked)
            {
                IoC.Initialize();
            }
            _applicationScope = AsyncScopedLifestyle.BeginScope(IoC.Container);
            _appSettings = IoC.Container.GetInstance<AppSettings>();
        }

        public void Run()
        {
            try
            {
                CancellationTokenSource = new CancellationTokenSource();

                // Configure window
                _window = IoC.Container.GetInstance<IWindow>();
                _window.Title = _appSettings.Name;
                _window.Size = _appSettings.LoadSize;
                _window.IsEventDriven = false;

              

                _window.Load += OnWindowLoad;
                _window.Closing += Dispose;
                _window.Run();
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Application failed to run");
                throw;
            }
        }

        private void OnWindowLoad()
        {
            // Initialize Vulkan synchronization context first
            _vulkanSyncContext = IoC.Container.GetInstance<VulkanSynchronizationContext>();
            _world = IoC.Container.GetInstance<World>();
            _assimpLoader = IoC.Container.GetInstance<AssimpLoader>();
            // Initialize all components that need Vulkan on the Vulkan thread
            _vulkanSyncContext.ExecuteBlocking(() =>
            {
                InitializeVulkanComponents();
            });

            // Start async initialization
            _ = InitializeAsync();

            // Set up rendering
            SetupRendering();
        }

        private void InitializeVulkanComponents()
        {
            _logger.Info("Initializing Vulkan components on Vulkan thread...");

            _inputContext = IoC.Container.GetInstance<IInputContext>();
            _context = IoC.Container.GetInstance<VulkanContext>();
            _graphicsEngine = IoC.Container.GetInstance<GraphicsContext>();
            _pipelineManager = IoC.Container.GetInstance<PipelineManager>();
            _renderer = IoC.Container.GetInstance<WorldRenderer>();
            _shaderManager = IoC.Container.GetInstance<IShaderManager>();
            _coroutineScheduler = IoC.Container.GetInstance<CoroutineScheduler>();
            _assetManager = IoC.Container.GetInstance<AssetManager>();
            _layerStack = IoC.Container.GetInstance<LayerStack>();

            // Initialize performance tracing
            PerformanceTracer.Initialize(_context);

            // Set up asset manager
            _assetManager.OnProjectLoaded += (_) =>
            {
                DefaultMeshes.Initalize(_assetManager);
            };

            _logger.Info("Vulkan components initialized");
        }

        private async Task InitializeAsync()
        {
            _currentState = ApplicationState.Loading;
            Stopwatch loadWatch = Stopwatch.StartNew();

            try
            {
                _logger.Info("Starting asynchronous initialization...");

                // Compile shaders - this can run on any thread since it uses external processes
                _logger.Info("Compiling shaders...");
                await _shaderManager.CompileAllShadersAsync().ConfigureAwait(false);
                _logger.Info("Shaders compiled successfully");

                // Initialize renderer and world on Vulkan thread
                await _vulkanSyncContext.ExecuteAsync(async () =>
                {
                    await _renderer.InitializeAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                await _vulkanSyncContext.ExecuteAsync(async () =>
                {
                    await _world.Start(_renderer).ConfigureAwait(false);
                }).ConfigureAwait(false);

                // Load application-specific content
                _logger.Info("Loading application content...");
                await Load().ConfigureAwait(false);

                _currentState = ApplicationState.Running;
                _loadingCompleted.SetResult(true);

                loadWatch.Stop();
                _logger.Info($"Application fully initialized in: {loadWatch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Asynchronous initialization failed");
                _loadingCompleted.SetException(ex);
            }
        }

        private void SetupRendering()
        {
            _window.Update += OnUpdate;
            _window.Render += OnRender;
        }

        private void OnUpdate(double deltaTime)
        {
            try
            {
                // Process any pending Vulkan operations
                _vulkanSyncContext.ProcessPendingOperations();

                if (_currentState != ApplicationState.Running)
                {
                    UpdateLoadingState(deltaTime);
                    return;
                }

                // Execute update on Vulkan thread asynchronously
                _ = _vulkanSyncContext.ExecuteAsync(() => Update(deltaTime));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception in Update");
            }
        }

        private void OnRender(double time)
        {
            try
            {
                if (_currentState != ApplicationState.Running)
                {
                    //RenderLoadingScreen(time);
                    return;
                }

                // Execute render on Vulkan thread and wait for completion
                // (we need to wait because we can't have multiple frames in flight simultaneously)
                _vulkanSyncContext.ExecuteBlocking(() => Render(time).GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception in Render");
            }
        }

        protected virtual void UpdateLoadingState(double deltaTime)
        {
            Time.Update(_window.Time, deltaTime);
            _coroutineScheduler?.Update();
        }

        protected virtual void RenderLoadingScreen(double time)
        {
            // Render loading screen on Vulkan thread
            _vulkanSyncContext.ExecuteBlocking(() =>
            {
                try
                {
                    var batch = _graphicsEngine?.Begin();
                    if (batch != null)
                    {
                        PerformanceTracer.ProcessQueries(_context, _graphicsEngine.FrameIndex);
                        PerformanceTracer.BeginFrame(_graphicsEngine.FrameIndex);

                        _layerStack?.RenderImGui(batch.CommandBuffer);
                        _graphicsEngine.SubmitAndPresent(batch);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error rendering loading screen");
                }
            });
        }

        protected virtual async Task Load()
        {
            foreach (var item in IoC.Container.GetAllInstances<ILayer>())
            {
                await _layerStack.PushLayer(item);
            }
        }

        protected virtual async Task Update(double deltaTime)
        {
            using (PerformanceTracer.BeginSection("Update"))
            {
                Time.Update(_window.Time, deltaTime);
                _layerStack.Update();
                _coroutineScheduler.Update();

                await _world.Update(_renderer).ConfigureAwait(false);
                await _renderer.UpdateFrameData().ConfigureAwait(false);
            }
        }

        protected virtual async Task Render(double time)
        {
            using (PerformanceTracer.BeginSection("Whole Render"))
            {
                if (_layerStack.Count == 0)
                {
                    return;
                }

                var batch = _graphicsEngine.Begin();
                if (batch is null)
                {
                    return;
                }

                var frameIndex = _renderer.FrameIndex;

                PerformanceTracer.ProcessQueries(_context, frameIndex);
                PerformanceTracer.BeginFrame(frameIndex);

                using (PerformanceTracer.BeginSection("_layerStack.RenderImGui"))
                {
                    using (PerformanceTracer.BeginSection("RenderImGui", batch.CommandBuffer, frameIndex))
                    {
                        await _layerStack.RenderImGui(batch.CommandBuffer);
                    }
                }

                // Execute render operations sequentially to avoid threading issues
                using (PerformanceTracer.BeginSection("_layerStack.Render"))
                {
                    using (PerformanceTracer.BeginSection("_layerStack.Render", batch.CommandBuffer, frameIndex))
                    {
                        _layerStack.Render(batch.CommandBuffer);
                    }
                }

                using (PerformanceTracer.BeginSection("_renderer.Render()"))
                {
                    await _renderer.Render().ConfigureAwait(false);
                }

                using (PerformanceTracer.BeginSection("_graphicsEngine.end & Submit"))
                {
                    _graphicsEngine.SubmitAndPresent(batch);
                }
            }
        }

        public Task PushLayer(ILayer layer) => _layerStack.PushLayer(layer);
        public void PopLayer(ILayer layer) => _layerStack.PopLayer(layer);

        public virtual void Dispose()
        {
            _currentState = ApplicationState.ShuttingDown;

            CancellationTokenSource?.Cancel();
            _applicationCts?.Cancel();

            // Wait for loading to complete
            if (!_loadingCompleted.Task.IsCompleted)
            {
                Task.Run(async () =>
                {
                    await Task.WhenAny(_loadingCompleted.Task, Task.Delay(5000));
                }).GetAwaiter().GetResult();
            }

            // Dispose Vulkan context on Vulkan thread
            _vulkanSyncContext?.ExecuteBlocking(() =>
            {
                _context?.Device.GraphicsQueue.WaitIdle();
                _context?.Device.ComputeQueue.WaitIdle();

                _renderer?.Dispose();
                _graphicsEngine?.Dispose();
                _context?.Dispose();
            });

            _vulkanSyncContext?.Dispose();

            if (_window != null)
            {
                _window.Close();
                _window.Dispose();
            }

            _applicationScope?.Dispose();
            _container?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}*/

// OLD SOLUTION - working but cpu stall
/*using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.Coroutines;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

using System.Diagnostics;

namespace RockEngine.Core
{
    public abstract class Application : IDisposable
    {
        protected VulkanContext _context;
        protected IWindow _window;
        protected LayerStack _layerStack;
        protected GraphicsContext _graphicsEngine;
        protected IInputContext _inputContext;
        protected WorldRenderer _renderer;
        protected World _world;
        protected PipelineManager _pipelineManager;
        protected AssimpLoader _assimpLoader;
        protected readonly AppSettings _appSettings;

        private IShaderManager _shaderManager;
        private Task[] _renderTasks;
        private CoroutineScheduler _coroutineScheduler;
        private AssetManager _assetManager;
        private readonly Container _container;
        private readonly Scope _applicationScope;

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken CancellationToken => CancellationTokenSource.Token;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();



        public Application()
        {
            // Initialize DI container if not already initialized
            if (!IoC.Container.IsLocked)
            {
                IoC.Initialize();
            }
            _applicationScope = AsyncScopedLifestyle.BeginScope(IoC.Container);

            _appSettings = IoC.Container.GetInstance<AppSettings>();
        }

        public void Run()
        {
            CancellationTokenSource = new CancellationTokenSource();
            // Configure window
            _window = IoC.Container.GetInstance<IWindow>();
            _window.Title = _appSettings.Name;
            _window.Size = _appSettings.LoadSize;
            _window.IsEventDriven = false;

            // Resolve other dependencies
            _world = IoC.Container.GetInstance<World>();
            _assimpLoader = IoC.Container.GetInstance<AssimpLoader>();
            _window.Load += () =>
            {
                try
                {
                    OnWindowLoad().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to load window.");
                    throw;
                }
            };

            _window.Run();
        }

        private async Task OnWindowLoad()
        {
            Stopwatch loadWatch = Stopwatch.StartNew();
            _inputContext = IoC.Container.GetInstance<IInputContext>();

            // Resolve window-dependent components
            _context = IoC.Container.GetInstance<VulkanContext>();
            _graphicsEngine = IoC.Container.GetInstance<GraphicsContext>();
            _pipelineManager = IoC.Container.GetInstance<PipelineManager>();
            _renderer = IoC.Container.GetInstance<WorldRenderer>();
            _shaderManager = IoC.Container.GetInstance<IShaderManager>();
            _renderTasks = new Task[2];
            _coroutineScheduler = IoC.Container.GetInstance<CoroutineScheduler>();
            _assetManager = IoC.Container.GetInstance<AssetManager>();


            await _shaderManager.CompileAllShadersAsync();

            PerformanceTracer.Initialize(_context);
            PerformanceTracer.ProcessQueries(_context, _graphicsEngine.FrameIndex);
            PerformanceTracer.BeginFrame(_graphicsEngine.FrameIndex);
            _assetManager.OnProjectLoaded += (_) =>
            {
                DefaultMeshes.Initalize(_assetManager);
            };
            await _renderer.InitializeAsync().ConfigureAwait(false);
            await _world.Start(_renderer).ConfigureAwait(false);

            _logger.Info($"Core systems initialized in: {loadWatch.ElapsedMilliseconds} ms");

            _layerStack = IoC.Container.GetInstance<LayerStack>();


            await Load().ConfigureAwait(false);
            _window.Update += (s) =>
            {
                try
                {
                    Update(s).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception in Update. Closing application. :{0}, \n {1}", ex.Message, ex.StackTrace);
                    throw;
                }
            };

            _window.Render += (s) =>
            {
                try
                {
                    Render(s).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception in Render. Closing application. :{0}, \n {1}", ex.Message, ex.StackTrace);
                    throw;
                }
            };

            loadWatch.Stop();
            _logger.Info($"Application loaded in: {loadWatch.ElapsedMilliseconds} ms");

        }

        protected virtual async Task Load()
        {
            foreach (var item in IoC.Container.GetAllInstances<ILayer>())
            {
                await _layerStack.PushLayer(item);
            }
        }

        protected virtual async Task Update(double deltaTime)
        {
            using (PerformanceTracer.BeginSection("Update"))
            {
                Time.Update(_window.Time, deltaTime);
                _layerStack.Update();
                _coroutineScheduler.Update();
                await _world.Update(_renderer).ConfigureAwait(false);
                await _renderer.UpdateFrameData().ConfigureAwait(false);
            }
        }

        protected virtual async Task Render(double time)
        {
            using (PerformanceTracer.BeginSection("Whole Render"))
            {
                if (_layerStack.Count == 0)
                {
                    return;
                }

                var batch = _graphicsEngine.Begin();
                if (batch is null)
                {
                    return;
                }

                var frameIndex = _renderer.FrameIndex;

                PerformanceTracer.ProcessQueries(_context, frameIndex);
                PerformanceTracer.BeginFrame(frameIndex);


                using (PerformanceTracer.BeginSection("_layerStack.RenderImGui"))
                {
                    using (PerformanceTracer.BeginSection("RenderImGui", batch.CommandBuffer, frameIndex))
                    {
                        // Use ConfigureAwait(true) to ensure we continue on the ImGui context
                        await _layerStack.RenderImGui(batch.CommandBuffer);
                    }
                }


                _renderTasks[0] = Task.Run(() => {
                    using (PerformanceTracer.BeginSection("_layerStack.Render"))
                    {
                        var renderBatch = _context.GraphicsSubmitContext.CreateBatch();
                        using (PerformanceTracer.BeginSection("_layerStack.Render", renderBatch.CommandBuffer, frameIndex))
                        {
                            _layerStack.Render(renderBatch.CommandBuffer);
                        }
                        renderBatch.Submit();
                    }
                });

                _renderTasks[1] = Task.Run(async () =>
                {
                    using (PerformanceTracer.BeginSection("_renderer.Render()"))
                    {
                        await _renderer.Render().ConfigureAwait(false);
                    }
                });


                using (PerformanceTracer.BeginSection("_graphicsEngine.end & Submit"))
                {
                    await Task.WhenAll(_renderTasks).ConfigureAwait(false);
                    _graphicsEngine.SubmitAndPresent(batch);
                }
            }
        }

        public Task PushLayer(ILayer layer) => _layerStack.PushLayer(layer);
        public void PopLayer(ILayer layer) => _layerStack.PopLayer(layer);


        public virtual void Dispose()
        {
            CancellationTokenSource?.Cancel();
            _context.Device.GraphicsQueue.WaitIdle();
            _context.Device.ComputeQueue.WaitIdle();

            _renderer?.Dispose();
            _graphicsEngine?.Dispose();
            _context?.Dispose();

            if (_window != null)
            {
                _window.Close();
                _window.Dispose();
            }

            _applicationScope?.Dispose();
            _container?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
*/