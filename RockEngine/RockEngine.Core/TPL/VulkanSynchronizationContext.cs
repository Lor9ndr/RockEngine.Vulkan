using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using NLog;

namespace RockEngine.Core.TPL
{
    /// <summary>
    /// Enhanced Vulkan Synchronization Context with improved stability, performance, and deadlock prevention
    /// </summary>
    public sealed class VulkanSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<WorkItem> _queue = new();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly Thread _vulkanThread;
        private readonly AutoResetEvent _workAvailable = new AutoResetEvent(false);
        private readonly ManualResetEventSlim _threadReady = new ManualResetEventSlim(false);
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly object _disposeLock = new object();
        private readonly int _maxBatchSize = 100; // Process up to 100 items per batch
        private volatile bool _disposed;
        private long _operationCount;
        private long _processedCount;
        private Exception _fatalException;

        public Thread VulkanThread => _vulkanThread;
        public bool IsOnVulkanThread => Thread.CurrentThread == _vulkanThread;
        public long OperationCount => Interlocked.Read(ref _operationCount);
        public long ProcessedCount => Interlocked.Read(ref _processedCount);
        public bool HasFatalException => _fatalException != null;

        // Performance monitoring
        private long _totalQueueTimeTicks;
        private long _totalExecutionTimeTicks;
        private long _maxQueueTimeTicks;

        public TimeSpan AverageQueueTime => TimeSpan.FromTicks(Interlocked.Read(ref _totalQueueTimeTicks) / Math.Max(1, _processedCount));
        public TimeSpan AverageExecutionTime => TimeSpan.FromTicks(Interlocked.Read(ref _totalExecutionTimeTicks) / Math.Max(1, _processedCount));
        public TimeSpan MaxQueueTime => TimeSpan.FromTicks(Interlocked.Read(ref _maxQueueTimeTicks));

        private abstract class WorkItem
        {
            public readonly long CreationTime = DateTime.UtcNow.Ticks;
            public abstract void Execute();
        }

        private sealed class SyncWorkItem : WorkItem
        {
            private readonly Action _action;
            private readonly TaskCompletionSource<bool> _completion;

            public SyncWorkItem(Action action, TaskCompletionSource<bool> completion)
            {
                _action = action;
                _completion = completion;
            }

            public override void Execute()
            {
                try
                {
                    _action();
                    _completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _completion.TrySetException(ex);
                }
            }
        }

        private sealed class AsyncWorkItem : WorkItem
        {
            private readonly Func<Task> _asyncAction;
            private readonly TaskCompletionSource<bool> _completion;

            public AsyncWorkItem(Func<Task> asyncAction, TaskCompletionSource<bool> completion)
            {
                _asyncAction = asyncAction;
                _completion = completion;
            }

            public override async void Execute()
            {
                try
                {
                    await _asyncAction().ConfigureAwait(false);
                    _completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _completion.TrySetException(ex);
                }
            }
        }

        private sealed class PostWorkItem : WorkItem
        {
            private readonly SendOrPostCallback _callback;
            private readonly object _state;

            public PostWorkItem(SendOrPostCallback callback, object state)
            {
                _callback = callback;
                _state = state;
            }

            public override void Execute()
            {
                _callback(_state);
            }
        }

        private sealed class TimedWorkItem : WorkItem
        {
            private readonly Action _action;
            private readonly Action<TimeSpan> _completionCallback;

            public TimedWorkItem(Action action, Action<TimeSpan> completionCallback)
            {
                _action = action;
                _completionCallback = completionCallback;
            }

            public override void Execute()
            {
                var startTime = DateTime.UtcNow;
                try
                {
                    _action();
                }
                finally
                {
                    var duration = DateTime.UtcNow - startTime;
                    _completionCallback?.Invoke(duration);
                }
            }
        }

        /// <summary>
        /// Enhanced dispatcher for Vulkan operations with better error handling and performance monitoring
        /// </summary>
        public sealed class VulkanDispatcher
        {
            private readonly VulkanSynchronizationContext _context;

            internal VulkanDispatcher(VulkanSynchronizationContext context)
            {
                _context = context;
            }

            public bool IsOnVulkanThread => _context.IsOnVulkanThread;
            public Task ExecuteAsync(Action action) => _context.ExecuteAsync(action);
            public Task ExecuteAsync(Func<Task> asyncAction) => _context.ExecuteAsync(asyncAction);
            public void ExecuteNonBlocking(Action action) => _context.ExecuteNonBlocking(action);
            public Task<T> ExecuteAsync<T>(Func<T> func) => _context.ExecuteAsync(func);
            public Task<T> ExecuteAsync<T>(Func<Task<T>> asyncFunc) => _context.ExecuteAsync(asyncFunc);
        }

        public VulkanSynchronizationContext(string name = "VulkanThread", ThreadPriority priority = ThreadPriority.Normal)
        {
            // Create a dedicated thread for Vulkan operations
            _vulkanThread = new Thread(VulkanThreadMain)
            {
                Name = name,
                IsBackground = false,
                Priority = priority
            };

            _vulkanThread.Start();

            // Wait for thread to initialize and be ready
            if (!_threadReady.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Vulkan thread failed to initialize within timeout");
            }
        }

        private void VulkanThreadMain()
        {
            try
            {
                // Set this context as the current synchronization context for the Vulkan thread
                SetSynchronizationContext(this);
                _threadReady.Set();

                _logger.Info($"Vulkan thread started: {Thread.CurrentThread.ManagedThreadId}");

                var spinWait = new SpinWait();
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Process available work items in batches for better performance
                    if (!ProcessWorkItemsBatch())
                    {
                        // No work available, wait for signal with timeout
                        _workAvailable.WaitOne(16); // ~60fps check rate
                    }

                    // Use spin wait for high-frequency operations to reduce context switching
                    spinWait.SpinOnce();
                    if (spinWait.NextSpinWillYield)
                    {
                        spinWait.Reset();
                    }
                }

                // Process any remaining items before shutdown
                while (ProcessWorkItemsBatch() && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Drain the queue
                }
            }
            catch (Exception ex)
            {
                _fatalException = ex;
                _logger.Fatal(ex, "Vulkan thread crashed");
                throw;
            }
            finally
            {
                _logger.Info("Vulkan thread stopped");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ProcessWorkItemsBatch()
        {
            bool processedAny = false;
            int processedCount = 0;

            while (processedCount < _maxBatchSize && _queue.TryDequeue(out var item))
            {
                try
                {
                    // Calculate queue time for performance monitoring
                    var queueTime = DateTime.UtcNow.Ticks - item.CreationTime;
                    Interlocked.Add(ref _totalQueueTimeTicks, queueTime);

                    var maxQueueTime = Interlocked.Read(ref _maxQueueTimeTicks);
                    if (queueTime > maxQueueTime)
                    {
                        Interlocked.CompareExchange(ref _maxQueueTimeTicks, queueTime, maxQueueTime);
                    }

                    var executionStart = DateTime.UtcNow.Ticks;
                    item.Execute();
                    var executionTime = DateTime.UtcNow.Ticks - executionStart;

                    Interlocked.Add(ref _totalExecutionTimeTicks, executionTime);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing Vulkan operation");
                }
                finally
                {
                    Interlocked.Decrement(ref _operationCount);
                    Interlocked.Increment(ref _processedCount);
                    processedCount++;
                    processedAny = true;
                }
            }

            return processedAny;
        }

        /// <summary>
        /// Gets the dispatcher for Vulkan operations
        /// </summary>
        public VulkanDispatcher Dispatcher => new VulkanDispatcher(this);

        /// <summary>
        /// Processes all pending Vulkan operations with optional timeout
        /// </summary>
        public bool ProcessPendingOperations(TimeSpan timeout = default)
        {
            if (IsOnVulkanThread)
            {
                throw new InvalidOperationException("ProcessPendingOperations should not be called from the Vulkan thread");
            }

            if (timeout == default)
                timeout = TimeSpan.FromSeconds(5);

            var startTime = DateTime.UtcNow;
            while (OperationCount > 0 && (DateTime.UtcNow - startTime) < timeout)
            {
                _workAvailable.Set();
                Thread.Sleep(1);
            }

            return OperationCount == 0;
        }

        /// <summary>
        /// Executes an operation on the Vulkan thread and waits for completion with timeout
        /// </summary>
        public void ExecuteBlocking(Action action, TimeSpan timeout = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VulkanSynchronizationContext));

            if (HasFatalException)
                throw new InvalidOperationException("Vulkan thread has encountered a fatal exception", _fatalException);

            if (IsOnVulkanThread)
            {
                action();
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            var workItem = new SyncWorkItem(action, tcs);

            _queue.Enqueue(workItem);
            Interlocked.Increment(ref _operationCount);
            _workAvailable.Set();

            if (timeout == default)
                timeout = TimeSpan.FromSeconds(30);

            if (!tcs.Task.Wait(timeout))
            {
                throw new TimeoutException($"Vulkan operation timed out after {timeout.TotalSeconds} seconds");
            }
        }

        /// <summary>
        /// Non-blocking version that processes work and returns immediately
        /// </summary>
        public void ExecuteNonBlocking(Action action)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VulkanSynchronizationContext));

            if (HasFatalException)
                throw new InvalidOperationException("Vulkan thread has encountered a fatal exception", _fatalException);

            if (IsOnVulkanThread)
            {
                action();
                return;
            }

            _queue.Enqueue(new PostWorkItem(_ => action(), null));
            Interlocked.Increment(ref _operationCount);
            _workAvailable.Set();
        }

        /// <summary>
        /// Executes an operation on the Vulkan thread asynchronously
        /// </summary>
        public Task ExecuteAsync(Action action)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VulkanSynchronizationContext));

            if (HasFatalException)
                throw new InvalidOperationException("Vulkan thread has encountered a fatal exception", _fatalException);

            if (IsOnVulkanThread)
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            _queue.Enqueue(new SyncWorkItem(action, tcs));
            Interlocked.Increment(ref _operationCount);
            _workAvailable.Set();

            return tcs.Task;
        }

        /// <summary>
        /// Executes a function on the Vulkan thread asynchronously and returns result
        /// </summary>
        public Task<T> ExecuteAsync<T>(Func<T> func)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VulkanSynchronizationContext));

            if (HasFatalException)
                throw new InvalidOperationException("Vulkan thread has encountered a fatal exception", _fatalException);

            if (IsOnVulkanThread)
            {
                return Task.FromResult(func());
            }

            var tcs = new TaskCompletionSource<T>();
            _queue.Enqueue(new SyncWorkItem(() =>
            {
                try
                {
                    var result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, new TaskCompletionSource<bool>()));

            Interlocked.Increment(ref _operationCount);
            _workAvailable.Set();

            return tcs.Task;
        }

        /// <summary>
        /// Executes an async operation on the Vulkan thread
        /// </summary>
        public Task ExecuteAsync(Func<Task> asyncAction)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VulkanSynchronizationContext));

            if (HasFatalException)
                throw new InvalidOperationException("Vulkan thread has encountered a fatal exception", _fatalException);

            if (IsOnVulkanThread)
            {
                return asyncAction();
            }

            var tcs = new TaskCompletionSource<bool>();
            _queue.Enqueue(new AsyncWorkItem(asyncAction, tcs));
            Interlocked.Increment(ref _operationCount);
            _workAvailable.Set();

            return tcs.Task;
        }

        /// <summary>
        /// Executes an async function on the Vulkan thread and returns result
        /// </summary>
        public Task<T> ExecuteAsync<T>(Func<Task<T>> asyncFunc)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VulkanSynchronizationContext));

            if (HasFatalException)
                throw new InvalidOperationException("Vulkan thread has encountered a fatal exception", _fatalException);

            if (IsOnVulkanThread)
            {
                return asyncFunc();
            }

            var tcs = new TaskCompletionSource<T>();
            _queue.Enqueue(new AsyncWorkItem(async () =>
            {
                try
                {
                    var result = await asyncFunc().ConfigureAwait(false);
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, new TaskCompletionSource<bool>()));

            Interlocked.Increment(ref _operationCount);
            _workAvailable.Set();

            return tcs.Task;
        }

        /// <summary>
        /// Executes an operation with timing information
        /// </summary>
        public Task ExecuteTimedAsync(Action action, Action<TimeSpan> onCompleted = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VulkanSynchronizationContext));

            if (HasFatalException)
                throw new InvalidOperationException("Vulkan thread has encountered a fatal exception", _fatalException);

            if (IsOnVulkanThread)
            {
                var startTime = DateTime.UtcNow;
                try
                {
                    action();
                }
                finally
                {
                    onCompleted?.Invoke(DateTime.UtcNow - startTime);
                }
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            _queue.Enqueue(new TimedWorkItem(action, duration =>
            {
                onCompleted?.Invoke(duration);
                tcs.TrySetResult(true);
            }));

            Interlocked.Increment(ref _operationCount);
            _workAvailable.Set();

            return tcs.Task;
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VulkanSynchronizationContext));

            if (HasFatalException)
                throw new InvalidOperationException("Vulkan thread has encountered a fatal exception", _fatalException);

            _queue.Enqueue(new PostWorkItem(d, state!));
            Interlocked.Increment(ref _operationCount);
            _workAvailable.Set();
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            ExecuteBlocking(() => d(state));
        }

        /// <summary>
        /// Waits for all pending operations to complete with timeout
        /// </summary>
        public bool WaitForPendingOperations(TimeSpan timeout = default)
        {
            if (IsOnVulkanThread)
                throw new InvalidOperationException("Cannot wait for operations from Vulkan thread");

            return ProcessPendingOperations(timeout);
        }

        /// <summary>
        /// Resets performance statistics
        /// </summary>
        public void ResetStatistics()
        {
            Interlocked.Exchange(ref _totalQueueTimeTicks, 0);
            Interlocked.Exchange(ref _totalExecutionTimeTicks, 0);
            Interlocked.Exchange(ref _maxQueueTimeTicks, 0);
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;

                _disposed = true;
                _cancellationTokenSource.Cancel();
                _workAvailable.Set();

                try
                {
                    if (_vulkanThread.IsAlive && !_vulkanThread.Join(5000))
                    {
                        _logger.Warn("Vulkan thread did not exit gracefully, attempting to abort");
                        try
                        {
                            _vulkanThread.Abort();
                        }
                        catch (PlatformNotSupportedException)
                        {
                            _logger.Warn("Thread abort is not supported on this platform");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during Vulkan thread shutdown");
                }
                finally
                {
                    _cancellationTokenSource.Dispose();
                    _workAvailable.Dispose();
                    _threadReady.Dispose();

                    _logger.Info("Vulkan synchronization context disposed");
                }
            }
        }
    }
}