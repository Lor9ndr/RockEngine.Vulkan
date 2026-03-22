using System.Runtime.CompilerServices;

namespace RockEngine.Vulkan
{

    /// <summary>
    /// Represents a completed GPU operation that can be waited on and manages resource cleanup
    /// Supports <see cref="TaskAwaiter"/>
    /// </summary>
    public sealed class SubmitOperation : IDisposable, IAsyncDisposable
    {
        private readonly SubmitContext _context;
        private readonly List<UploadBatch> _batches;
        private readonly List<IDisposable> _disposables;
        private readonly List<VkSemaphore> _semaphores;
        private VkFence _fence;
        private bool _completed;

        public VkFence Fence => _fence;
        public bool IsCompleted => _completed;
        private readonly Lock _lock = new Lock();

        internal SubmitOperation(
            SubmitContext context,
            VkFence fence,
            List<UploadBatch> batches,
            List<IDisposable> disposables,
            List<VkSemaphore> semaphores)
        {
            _context = context;
            _fence = fence;
            _batches = batches;
            _disposables = disposables;
            _semaphores = semaphores;
        }

        public void Wait()
        {
            if (_completed) return;
            if (_fence != null && !_fence.IsDisposed)
            {
                _fence.Wait();
            }
            Complete();
        }

        private async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            if (_completed) return;
            if (_fence != null && !_fence.IsDisposed)
            {
                await _fence.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            Complete();
        }

        public TaskAwaiter GetAwaiter() => WaitAsync().GetAwaiter();
        public Task AsTask() => WaitAsync();

        private void Complete()
        {
           if (_completed) return;
            lock (_lock)
            {
                if (_completed) return;

                // Dispose all user‑provided disposables
                foreach (var d in _disposables) d.Dispose();

                // Return batches to their pools
                foreach (var b in _batches) _context.ReturnBatchToPool(b);

                _fence = null;

                // Clear lists (semaphores are just references, not disposed)
                _batches.Clear();
                _disposables.Clear();
                _semaphores.Clear();
                _completed = true;
            }
           
        }

        public void Dispose()
        {
            if (!_completed) Wait();   // synchronous wait, but user should have awaited
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed) await WaitAsync();
            GC.SuppressFinalize(this);
        }
    }
}