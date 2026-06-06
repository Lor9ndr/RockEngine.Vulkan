using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace RockEngine.Core.Synchronization
{
    public sealed class MainThreadSynchronizationContext : SynchronizationContext
    {

        // General queue used by Post and Send (continuations, Task.Run, etc.)
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _generalQueue = new();

        // Phase‑specific queues – drained explicitly in Update/Render
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _updateQueue = new();
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _renderQueue = new();

        private static MainThreadSynchronizationContext? _current;

        public static new MainThreadSynchronizationContext? Current
        {
            get => _current;
            private set => _current = value;
        }

        /// <summary>
        /// Install this context on the calling (main) thread.
        /// Call <see cref="ProcessAllQueuedWork"/> regularly to drain the general queue.
        /// </summary>
        public static MainThreadSynchronizationContext Install()
        {
            if (Current != null)
            {
                throw new InvalidOperationException(
                    "A MainThreadSynchronizationContext is already installed on this thread.");
            }

            var ctx = new MainThreadSynchronizationContext();
            SetSynchronizationContext(ctx);
            Current = ctx;
            return ctx;
        }
       
        /// <summary>
        /// Blocks the calling thread (which must be the main thread) until 
        /// the task completes, repeatedly pumping the general queue so that
        /// continuations scheduled on this SynchronizationContext can execute.
        /// </summary>
        public static void WaitOnMainThread(Task task)
        {
            var ctx = Current;

            // Fast path: already completed
            if (task.IsCompleted || ctx is null)
            {
                task.GetAwaiter().GetResult(); // re-throws exceptions
                return;
            }

            // Slow path: pump the queue until the task finishes
            while (!task.IsCompleted)
            {
                ctx?.ProcessAllQueuedWork();
                // Yield the thread for a tiny moment – avoids 100% CPU spin
                Thread.Yield();
            }

            // Now it's completed – propagate exception if any
            task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Drain the general queue (used by Post, Send, and async continuations).
        /// Call this at the beginning of every frame.
        /// </summary>
        public void ProcessAllQueuedWork()
        {
            while (_generalQueue.TryDequeue(out var item))
            {
                ExecuteCallback(item.Callback, item.State);
            }
        }

        /// <summary>
        /// Drain the Update‑phase queue. Call inside your Update handler where you want
        /// scheduled update work to execute.
        /// </summary>
        public void ProcessUpdateWork()
        {
            while (_updateQueue.TryDequeue(out var item))
            {
                ExecuteCallback(item.Callback, item.State);
            }
        }

        /// <summary>
        /// Drain the Render‑phase queue. Call inside your Render handler where you want
        /// scheduled render work to execute.
        /// </summary>
        public void ProcessRenderWork()
        {
            while (_renderQueue.TryDequeue(out var item))
            {
                ExecuteCallback(item.Callback, item.State);
            }
        }

        /// <summary>
        /// Schedule a callback to be executed during the next <see cref="ProcessUpdateWork"/>.
        /// </summary>
        public void RunOnUpdate(SendOrPostCallback callback, object? state)
        {
            _updateQueue.Enqueue((callback, state));
        }

        /// <summary>
        /// Schedule a callback to be executed during the next <see cref="ProcessRenderWork"/>.
        /// </summary>
        public void RunOnRender(SendOrPostCallback callback, object? state)
        {
            _renderQueue.Enqueue((callback, state));
        }


        public override void Post(SendOrPostCallback d, object? state)
        {
            _generalQueue.Enqueue((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            // Already on the main thread? Execute directly.
            if (Current == this && IsCurrentThreadMain())
            {
                d(state);
                return;
            }

            // Block the calling thread until the main thread processes the item.
            using var mre = new ManualResetEventSlim(false);
            Exception? capturedException = null;

            _generalQueue.Enqueue((s =>
            {
                try
                {
                    d(s);
                }
                catch (Exception ex)
                {
                    capturedException = ExceptionDispatchInfo.Capture(ex).SourceException;
                }
                finally
                {
                    mre.Set();
                }
            }, state));

            mre.Wait();

            if (capturedException != null)
            {
                ExceptionDispatchInfo.Throw(capturedException);
            }
        }

        /// <summary>
        /// Helper to run an async operation on the main thread and return a Task.
        /// The continuation will be posted to the general queue.
        /// </summary>
        public Task RunOnMainThread(Func<Task> function)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    await function().ConfigureAwait(false);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            return tcs.Task;
        }

        /// <summary>
        /// Synchronous version for non‑async actions (uses Send).
        /// </summary>
        public void RunOnMainThread(Action action)
        {
            Send(_ => action(), null);
        }

        private static bool IsCurrentThreadMain() => Current != null;

        private static void ExecuteCallback(SendOrPostCallback callback, object? state)
        {
            try
            {
                callback(state);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainThreadCtx] Unhandled exception: {ex}");
            }
        }
    }
}