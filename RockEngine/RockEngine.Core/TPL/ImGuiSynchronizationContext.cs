using NLog;

using System.Collections.Concurrent;

namespace RockEngine.Core.TPL
{
    /// <summary>
    /// Synchronization context that executes continuations on the main thread
    /// Perfect for ImGui and other single-threaded UI frameworks
    /// </summary>
    public class ImGuiSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback callback, object state)> _queue = new();
        private bool _disposed = false;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static void Initialize()
        {
            var context = new ImGuiSynchronizationContext();
            SetSynchronizationContext(context);
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            if (_disposed)
            {
                return;
            }

            _queue.Enqueue((d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            if (_disposed)
            {
                return;
            }

            if (Current == this)
            {
                // We're already on the main thread, execute directly
                d(state);
            }
            else
            {
                // We're on a different thread, use manual reset event to block
                using var resetEvent = new ManualResetEventSlim(false);
                Exception exception = null;

                Post(s =>
                {
                    try
                    {
                        d(s);
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                    finally
                    {
                        resetEvent.Set();
                    }
                }, state);

                resetEvent.Wait();

                if (exception != null)
                {
                    throw new AggregateException("Exception occurred during Send", exception);
                }
            }
        }

        /// <summary>
        /// Process all pending continuations on the main thread
        /// Call this once per frame in your main loop
        /// </summary>
        public void Update()
        {
            if (_disposed)
            {
                return;
            }

            // Process all available continuations
            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    item.callback(item.state);
                }
                catch (Exception ex)
                {
                    // Log the exception but continue processing other continuations
                    _logger.Error(ex, "Error in ImGui synchronization context continuation");
                }
            }
        }

        public override SynchronizationContext CreateCopy()
        {
            return this;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Clear any pending continuations
            while (_queue.TryDequeue(out _)) { }
        }
    }
}