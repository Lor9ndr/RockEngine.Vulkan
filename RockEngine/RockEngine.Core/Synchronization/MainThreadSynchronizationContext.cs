using System.Collections.Concurrent;

namespace RockEngine.Core.Synchronization
{
    public sealed class MainThreadSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _queue = new();
        private readonly Thread _mainThread;

        public MainThreadSynchronizationContext()
        {
            _mainThread = Thread.CurrentThread;
            SetSynchronizationContext(this); // Automatically sets itself as current
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            _queue.Enqueue((d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            if (Thread.CurrentThread == _mainThread)
            {
                d(state); // Execute synchronously if already on main thread
            }
            else
            {
                // Block until the main thread processes the callback
                using var mre = new ManualResetEventSlim(false);
                Post(_ =>
                {
                    d(state);
                    mre.Set();
                }, null);
                mre.Wait();
            }
        }

        /// <summary>
        /// Executes all pending callbacks on the current thread.
        /// Must be called periodically from the main thread.
        /// </summary>
        public void ExecutePending()
        {
            while (_queue.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }
}