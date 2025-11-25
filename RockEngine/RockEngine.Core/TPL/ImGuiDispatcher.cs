using System.Collections.Concurrent;
using System.Diagnostics;

namespace RockEngine.Core.TPL
{
    /// <summary>
    /// Single-threaded dispatcher that processes async work on the main thread
    /// Similar to WPF's Dispatcher or JavaScript's event loop
    /// </summary>
    public class SingleThreadDispatcher : IDisposable
    {
        private readonly ConcurrentQueue<DispatcherTask> _taskQueue = new();
        private readonly AutoResetEvent _waitHandle = new AutoResetEvent(false);
        private Thread _mainThread;
        private volatile bool _isRunning = true;
        private long _taskIdCounter = 0;

        public static SingleThreadDispatcher Current { get; private set; }

        public SingleThreadDispatcher()
        {
            _mainThread = Thread.CurrentThread;
            Current = this;
        }

        public Task InvokeAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            var task = new DispatcherTask(
                Interlocked.Increment(ref _taskIdCounter),
                () => { action(); tcs.SetResult(true); },
                DispatcherPriority.Normal
            );

            _taskQueue.Enqueue(task);
            _waitHandle.Set();

            return tcs.Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            var task = new DispatcherTask(
                Interlocked.Increment(ref _taskIdCounter),
                () => { tcs.SetResult(func()); },
                DispatcherPriority.Normal
            );

            _taskQueue.Enqueue(task);
            _waitHandle.Set();

            return tcs.Task;
        }

        public Task InvokeAsync(Func<Task> asyncFunc)
        {
            var tcs = new TaskCompletionSource<bool>();
            var task = new DispatcherTask(
                Interlocked.Increment(ref _taskIdCounter),
                async () =>
                {
                    await asyncFunc();
                    tcs.SetResult(true);
                },
                DispatcherPriority.Normal
            );

            _taskQueue.Enqueue(task);
            _waitHandle.Set();

            return tcs.Task;
        }

        public Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc)
        {
            var tcs = new TaskCompletionSource<T>();
            var task = new DispatcherTask(
                Interlocked.Increment(ref _taskIdCounter),
                async () =>
                {
                    var result = await asyncFunc();
                    tcs.SetResult(result);
                },
                DispatcherPriority.Normal
            );

            _taskQueue.Enqueue(task);
            _waitHandle.Set();

            return tcs.Task;
        }

        /// <summary>
        /// Process all pending tasks (call this once per frame in your main loop)
        /// </summary>
        public void ProcessQueue()
        {
            // Verify we're on the main thread
            if (Thread.CurrentThread != _mainThread)
            {
                throw new InvalidOperationException("ProcessQueue must be called from the main thread");
            }

            int processedCount = 0;
            const int maxProcessPerFrame = 100; // Prevent frame stalls

            while (_taskQueue.TryDequeue(out var task) && processedCount < maxProcessPerFrame)
            {
                try
                {
                    if (task.AsyncAction != null)
                    {
                        // For async tasks, we don't await here - they'll continue on whatever context they choose
                        _ = Task.Run(async () => await task.AsyncAction());
                    }
                    else
                    {
                        task.SyncAction?.Invoke();
                    }
                    processedCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Dispatcher task failed: {ex}");
                }
            }
        }

        /// <summary>
        /// Run the dispatcher on a separate thread (for console apps or services)
        /// </summary>
        public void RunOnDedicatedThread()
        {
            var dispatcherThread = new Thread(() =>
            {
                _mainThread = Thread.CurrentThread;

                while (_isRunning)
                {
                    ProcessQueue();
                    _waitHandle.WaitOne(16); // ~60fps
                }
            })
            {
                Name = "SingleThreadDispatcher",
                IsBackground = true
            };

            dispatcherThread.Start();
        }

        /// <summary>
        /// Non-blocking version for game loops - just processes available work
        /// </summary>
        public void ProcessPendingWork()
        {
            if (Thread.CurrentThread != _mainThread)
                return;

            ProcessQueue();
        }

        public void Dispose()
        {
            _isRunning = false;
            _waitHandle.Set();
            _waitHandle.Dispose();
        }

        private struct DispatcherTask
        {
            public long Id { get; }
            public Action SyncAction { get; }
            public Func<Task> AsyncAction { get; }
            public DispatcherPriority Priority { get; }

            public DispatcherTask(long id, Action syncAction, DispatcherPriority priority)
            {
                Id = id;
                SyncAction = syncAction;
                SyncAction.Invoke();
                AsyncAction = null;
                Priority = priority;
            }

            public DispatcherTask(long id, Func<Task> asyncAction, DispatcherPriority priority)
            {
                Id = id;
                SyncAction = null;
                AsyncAction = asyncAction;
                AsyncAction.Invoke();
                Priority = priority;
            }
        }
    }

    public enum DispatcherPriority
    {
        Low,
        Normal,
        High,
        Render,
        Input
    }
}