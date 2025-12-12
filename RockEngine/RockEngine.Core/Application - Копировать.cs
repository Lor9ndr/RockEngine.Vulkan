#region old
/*
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
                IoC.Initialize(this);
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
            _graphicsEngine.AddSwapchain(VkSwapchain.Create(_context, SurfaceHandler.CreateSurface(_window, _context)));
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

                var batch = _graphicsEngine.BeginFrame();
                if (batch is null)
                {
                    return;
                }

                var frameIndex = _renderer.FrameIndex;

                PerformanceTracer.ProcessQueries(_context, frameIndex);
                PerformanceTracer.BeginFrame(frameIndex);





                using (PerformanceTracer.BeginSection("_layerStack.RenderImGui"))




                {
                    using (PerformanceTracer.BeginSection("RenderImGui", batch, frameIndex))
                    {
                        _layerStack.RenderImGui(batch);
                    }
                }




                _renderTasks[0] = Task.Run(() => {
                    using (PerformanceTracer.BeginSection("_layerStack.Render"))
                    {
                        var renderBatch = _context.GraphicsSubmitContext.CreateBatch();
                        using (PerformanceTracer.BeginSection("_layerStack.Render", renderBatch, frameIndex))
                        {
                            _layerStack.Render(renderBatch);
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
                    await _graphicsEngine.SubmitAndPresentAsync();
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

            //if (Environment.CurrentManagedThreadId == Thread.CurrentThread.ManagedThreadId)
            //{
            //    return func();
            //}
            //else
            //{
            T result = default;
            _window.Invoke(() => result = func());
            return result;
            //}
        }

    }
}
*/
#endregion

#region new
/*using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.Coroutines;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.Extensions;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.TPL;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RockEngine.Core
{
    public interface IWindowThreadDispatcher
    {
        Task<T> InvokeAsync<T>(Func<T> action);
        Task InvokeAsync(Action action);
        Task InvokeAsync(Func<Task> action);
        void VerifyAccess();
        bool CheckAccess();
    }

    public class WindowThreadDispatcher : IWindowThreadDispatcher
    {
        private readonly SynchronizationContext _synchronizationContext;
        private readonly int _windowThreadId;

        public WindowThreadDispatcher(SynchronizationContext synchronizationContext, int windowThreadId)
        {
            _synchronizationContext = synchronizationContext;
            _windowThreadId = windowThreadId;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>();

            if (_synchronizationContext == null)
            {
                try
                {
                    var result = action();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                return tcs.Task;
            }

            _synchronizationContext.Post(async _ =>
            {
                try
                {
                    var result = action();
                    if (result is Task<T> taskResult)
                    {
                        var awaitedResult = await taskResult.ConfigureAwait(true);
                        tcs.SetResult(awaitedResult);
                    }
                    else
                    {
                        tcs.SetResult(result);
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public Task InvokeAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            if (_synchronizationContext == null)
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                return tcs.Task;
            }

            _synchronizationContext.Post(_ =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public Task InvokeAsync(Func<Task> action)
        {
            var tcs = new TaskCompletionSource<bool>();

            if (_synchronizationContext == null)
            {
                try
                {
                    action().ContinueWith(t =>
                    {
                        if (t.IsFaulted) tcs.SetException(t.Exception);
                        else if (t.IsCanceled) tcs.SetCanceled();
                        else tcs.SetResult(true);
                    });
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                return tcs.Task;
            }

            _synchronizationContext.Post(async _ =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public void VerifyAccess()
        {
            if (!CheckAccess())
            {
                throw new InvalidOperationException("Cross-thread operation not valid. This operation must be performed on the window thread.");
            }
        }

        public bool CheckAccess()
        {
            return _synchronizationContext == null || Environment.CurrentManagedThreadId == _windowThreadId;
        }
    }

    public class FrameSynchronizer : IDisposable
    {
        private readonly SemaphoreSlim _updateSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _renderSemaphore = new SemaphoreSlim(0, 1);
        private readonly object _syncLock = new object();
        private volatile bool _updateCompleted = false;
        private volatile bool _renderCompleted = false;
        private bool _disposed = false;

        public async Task<bool> WaitForUpdateAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return false;

            try
            {
                await _updateSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                lock (_syncLock)
                {
                    _updateCompleted = false;
                    _renderCompleted = false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void SignalUpdateCompleted()
        {
            lock (_syncLock)
            {
                _updateCompleted = true;
                if (!_renderCompleted)
                {
                    _renderSemaphore.Release();
                }
            }
        }

        public async Task<bool> WaitForRenderAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return false;

            try
            {
                await _renderSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void SignalRenderCompleted()
        {
            lock (_syncLock)
            {
                _renderCompleted = true;
                if (_updateCompleted && _renderCompleted)
                {
                    _updateSemaphore.Release();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _updateSemaphore?.Dispose();
            _renderSemaphore?.Dispose();
        }
    }

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
        private WindowThreadDispatcher _windowDispatcher;
        private TaskCompletionSource<bool> _windowLoadedTcs;
        private readonly FrameSynchronizer _frameSynchronizer;
        private readonly object _frameSyncLock = new object();
        private volatile bool _isUpdating;
        private volatile bool _isRendering;
        private long _currentFrameNumber = 0;

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken CancellationToken => CancellationTokenSource.Token;
        public IWindowThreadDispatcher WindowDispatcher => _windowDispatcher;
        public long CurrentFrameNumber => _currentFrameNumber;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public Application()
        {
            if (!IoC.Container.IsLocked)
            {
                IoC.Initialize(this);
            }
            _applicationScope = AsyncScopedLifestyle.BeginScope(IoC.Container);
            _appSettings = IoC.Container.GetInstance<AppSettings>();
            _windowLoadedTcs = new TaskCompletionSource<bool>();
            _frameSynchronizer = new FrameSynchronizer();

            // Create a temporary dispatcher that will be replaced when window loads
            _windowDispatcher = new WindowThreadDispatcher(null, Environment.CurrentManagedThreadId);
        }

        public async Task RunAsync()
        {
            CancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Create window directly first, then setup dispatcher properly
                _window = IoC.Container.GetInstance<IWindow>();
                _window.Title = _appSettings.Name;
                _window.Size = _appSettings.LoadSize;
                _window.IsEventDriven = false;

                _window.Load += OnWindowLoad;
                _window.Closing += OnWindowClosing;

                // Start the window - this will block until window is closed
                _window.Run();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to run application");
                throw;
            }
        }

        // Keep the original Run method for backward compatibility
        public void Run()
        {
            RunAsync().GetAwaiter().GetResult();
        }

        private async void OnWindowLoad()
        {
            try
            {
                // Capture the window thread synchronization context
                var syncContext = SynchronizationContext.Current;
                var windowThreadId = Environment.CurrentManagedThreadId;

                // Update the dispatcher with the proper synchronization context
                _windowDispatcher = new WindowThreadDispatcher(syncContext, windowThreadId);

                Stopwatch loadWatch = Stopwatch.StartNew();

                // Initialize dependencies on window thread
                _inputContext = IoC.Container.GetInstance<IInputContext>();
                _context = IoC.Container.GetInstance<VulkanContext>();
                _graphicsEngine = IoC.Container.GetInstance<GraphicsContext>();
                _pipelineManager = IoC.Container.GetInstance<PipelineManager>();
                _renderer = IoC.Container.GetInstance<WorldRenderer>();
                _shaderManager = IoC.Container.GetInstance<IShaderManager>();
                _renderTasks = new Task[2];
                _coroutineScheduler = IoC.Container.GetInstance<CoroutineScheduler>();
                _assetManager = IoC.Container.GetInstance<AssetManager>();
                _world = IoC.Container.GetInstance<World>();

                await _shaderManager.CompileAllShadersAsync().ConfigureAwait(true);

                PerformanceTracer.Initialize(_context);
                PerformanceTracer.ProcessQueries(_context, _graphicsEngine.FrameIndex);
                PerformanceTracer.BeginFrame(_graphicsEngine.FrameIndex);

                _assetManager.OnProjectLoaded += (_) =>
                {
                    _windowDispatcher.InvokeAsync(() => DefaultMeshes.Initalize(_assetManager));
                };

                await _renderer.InitializeAsync().ConfigureAwait(true);
                await _world.Start(_renderer).ConfigureAwait(true);

                _logger.Info($"Core systems initialized in: {loadWatch.ElapsedMilliseconds} ms");

                _layerStack = IoC.Container.GetInstance<LayerStack>();

                await Load().ConfigureAwait(true);

                // Setup event handlers
                _window.Update += OnUpdate;
                _window.Render += OnRender;

                loadWatch.Stop();
                _logger.Info($"Application loaded in: {loadWatch.ElapsedMilliseconds} ms");

                // Signal that window is fully loaded
                _windowLoadedTcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load window.");
                _windowLoadedTcs.TrySetException(ex);
                throw;
            }
        }

        private void OnWindowClosing()
        {
            Dispose();
        }

        private async void OnUpdate(double deltaTime)
        {
            if (CancellationToken.IsCancellationRequested) return;

            // Wait for previous frame to complete rendering
            if (!await _frameSynchronizer.WaitForUpdateAsync(CancellationToken).ConfigureAwait(true))
            {
                return;
            }

            lock (_frameSyncLock)
            {
                if (_isUpdating)
                {
                    _frameSynchronizer.SignalUpdateCompleted(); // Release if we're already updating
                    return;
                }
                _isUpdating = true;
            }

            try
            {
                Interlocked.Increment(ref _currentFrameNumber);
                await Update(deltaTime).ConfigureAwait(true);
                _frameSynchronizer.SignalUpdateCompleted();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception in Update. Closing application. :{0}, \n {1}", ex.Message, ex.StackTrace);
                _frameSynchronizer.SignalUpdateCompleted(); // Ensure we don't deadlock
                _window?.Close();
            }
            finally
            {
                lock (_frameSyncLock)
                {
                    _isUpdating = false;
                }
            }
        }

        private async void OnRender(double time)
        {
            if (CancellationToken.IsCancellationRequested) return;

            // Wait for update to complete before rendering
            if (!await _frameSynchronizer.WaitForRenderAsync(CancellationToken).ConfigureAwait(true))
            {
                return;
            }

            lock (_frameSyncLock)
            {
                if (_isRendering)
                {
                    _frameSynchronizer.SignalRenderCompleted(); // Release if we're already rendering
                    return;
                }
                _isRendering = true;
            }

            try
            {
                await Render(time).ConfigureAwait(true);
                _frameSynchronizer.SignalRenderCompleted();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception in Render. Closing application. :{0}, \n {1}", ex.Message, ex.StackTrace);
                _frameSynchronizer.SignalRenderCompleted(); // Ensure we don't deadlock
                _window?.Close();
            }
            finally
            {
                lock (_frameSyncLock)
                {
                    _isRendering = false;
                }
            }
        }

        protected virtual async Task Load()
        {
            var layers = IoC.Container.GetAllInstances<ILayer>();
            foreach (var layer in layers)
            {
                await _layerStack.PushLayer(layer).ConfigureAwait(true);
            }
        }

        protected virtual async Task Update(double deltaTime)
        {
            using (PerformanceTracer.BeginSection("Update"))
            {
                Time.Update(_window.Time, deltaTime);

                // Use the dispatcher for thread-sensitive operations
                if (_windowDispatcher.CheckAccess())
                {
                    _layerStack.Update();
                    _coroutineScheduler.Update();
                }
                else
                {
                    await _windowDispatcher.InvokeAsync(() =>
                    {
                        _layerStack.Update();
                        _coroutineScheduler.Update();
                    }).ConfigureAwait(true);
                }

                await _world.Update(_renderer).ConfigureAwait(true);
                await _renderer.UpdateFrameData().ConfigureAwait(true);
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

                // Execute ImGui rendering on window thread
                using (PerformanceTracer.BeginSection("_layerStack.RenderImGui"))
                {
                    using (batch.BeginSection("RenderImGui", frameIndex))
                    {
                        if (_windowDispatcher.CheckAccess())
                        {
                            await _layerStack.RenderImGui(batch);
                        }
                        else
                        {
                            await _windowDispatcher.InvokeAsync(() => _layerStack.RenderImGui(batch))
                                .ConfigureAwait(true);
                        }
                    }
                }

                // Parallel rendering tasks
                var renderTasks = new List<Task>();

                // Layer stack rendering
                var layerRenderTask = Task.Run(async () =>
                {
                    using (PerformanceTracer.BeginSection("_layerStack.Render"))
                    {
                        var renderBatch = _context.GraphicsSubmitContext.CreateBatch();
                        using (renderBatch.BeginSection("_layerStack.Render", frameIndex))
                        {
                            if (_windowDispatcher.CheckAccess())
                            {
                                _layerStack.Render(renderBatch);
                            }
                            else
                            {
                                await _windowDispatcher.InvokeAsync(() => _layerStack.Render(renderBatch))
                                    .ConfigureAwait(false);
                            }
                        }
                        renderBatch.Submit();
                    }
                });
                renderTasks.Add(layerRenderTask);

                // World rendering
                var worldRenderTask = Task.Run(async () =>
                {
                    using (PerformanceTracer.BeginSection("_renderer.Render()"))
                    {
                        await _renderer.Render().ConfigureAwait(false);
                    }
                });
                renderTasks.Add(worldRenderTask);

                await Task.WhenAll(renderTasks).ConfigureAwait(true);

                // Final submission and presentation
                using (PerformanceTracer.BeginSection("_graphicsEngine.end & Submit"))
                {
                    _graphicsEngine.Submit(batch);
                    _graphicsEngine.Present();
                }
            }
        }

        public Task PushLayer(ILayer layer) => _layerStack.PushLayer(layer);
        public void PopLayer(ILayer layer) => _layerStack.PopLayer(layer);

        /// <summary>
        /// Executes an action on the window thread and returns a task that completes when the action is done
        /// </summary>
        public Task RunOnWindowThreadAsync(Action action)
        {
            return _windowDispatcher?.InvokeAsync(action) ?? Task.Run(action);
        }

        /// <summary>
        /// Executes a function on the window thread and returns a task with the result
        /// </summary>
        public Task<T> RunOnWindowThreadAsync<T>(Func<T> function)
        {
            return _windowDispatcher?.InvokeAsync(function) ?? Task.Run(function);
        }

        /// <summary>
        /// Executes an async action on the window thread
        /// </summary>
        public Task RunOnWindowThreadAsync(Func<Task> asyncAction)
        {
            return _windowDispatcher?.InvokeAsync(asyncAction) ?? asyncAction();
        }

        /// <summary>
        /// Verifies that the current thread is the window thread
        /// </summary>
        public void VerifyWindowThread()
        {
            _windowDispatcher?.VerifyAccess();
        }

        /// <summary>
        /// Checks if the current thread is the window thread
        /// </summary>
        public bool IsOnWindowThread()
        {
            return _windowDispatcher?.CheckAccess() ?? true;
        }

        public virtual void Dispose()
        {
            CancellationTokenSource?.Cancel();
            _frameSynchronizer?.Dispose();

            // Ensure disposal happens on window thread if possible
            try
            {
                if (_windowDispatcher != null && !IsOnWindowThread())
                {
                    RunOnWindowThreadAsync(() => DisposeInternal()).GetAwaiter().GetResult();
                }
                else
                {
                    DisposeInternal();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error during threaded disposal, falling back to direct disposal");
                DisposeInternal();
            }

            _applicationScope?.Dispose();
            _container?.Dispose();
            GC.SuppressFinalize(this);
        }

        private void DisposeInternal()
        {
            try
            {
                _context?.Device.GraphicsQueue.WaitIdle();
                _context?.Device.ComputeQueue.WaitIdle();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error during device queue wait idle during disposal");
            }

            _renderer?.Dispose();
            _graphicsEngine?.Dispose();
            _context?.Dispose();

            if (_window != null)
            {
                try
                {
                    _window.Close();
                    _window.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Error during window disposal");
                }
            }

            CancellationTokenSource?.Dispose();
        }
    }

    // Extension methods for easier window thread dispatching
    public static class WindowThreadDispatcherExtensions
    {
        public static ConfiguredTaskAwaitable<T> ConfigureAwaitWindowThread<T>(this Task<T> task)
        {
            return task.ConfigureAwait(true);
        }

        public static ConfiguredTaskAwaitable ConfigureAwaitWindowThread(this Task task)
        {
            return task.ConfigureAwait(true);
        }

        public static async Task<T> ContinueOnWindowThread<T>(this Task<T> task, IWindowThreadDispatcher dispatcher)
        {
            var result = await task.ConfigureAwait(false);
            return await dispatcher.InvokeAsync(() => result).ConfigureAwait(false);
        }

        public static async Task ContinueOnWindowThread(this Task task, IWindowThreadDispatcher dispatcher)
        {
            await task.ConfigureAwait(false);
            await dispatcher.InvokeAsync(() => { }).ConfigureAwait(false);
        }
    }
}*/
#endregion


