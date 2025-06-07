using System.Runtime.CompilerServices;

namespace RockEngine.Vulkan
{
    /// <summary>
    /// Represents a completed GPU operation that can be waited on and manages resource cleanup
    /// Supports <see cref="TaskAwaiter"/>
    /// </summary>
    public sealed class FlushOperation : IDisposable, IAsyncDisposable
    {
        private readonly SubmitContext _context;
        private readonly VkFence _fence;
        private readonly List<UploadBatch> _batches;
        private readonly List<IDisposable> _disposables;
        private bool _completed;

        public VkFence Fence => _fence;
        public bool IsCompleted => _completed;

        internal FlushOperation(
            SubmitContext context,
            VkFence fence,
            List<UploadBatch> batches,
            List<IDisposable> disposables)
        {
            _context = context;
            _fence = fence;
            _batches = batches;
            _disposables = disposables;
        }

        /// <summary>
        /// Blocks until the GPU operation completes
        /// </summary>
        public void Wait()
        {
            if (_completed) return;
            _fence.Wait();
            Complete();
        }

        /// <summary>
        /// Asynchronously waits for the GPU operation to complete
        /// </summary>
        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            if (_completed) return;
            await _fence.WaitAsync(cancellationToken).ConfigureAwait(false);
            Complete();
        }

        public TaskAwaiter GetAwaiter() => WaitAsync().GetAwaiter();

        /// <summary>
        /// Cleans up resources and returns batches to pools
        /// </summary>
        private void Complete()
        {
            if (_completed) return;

            // Release all disposable resources
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }

            // Return batches to their respective pools
            foreach (var batch in _batches)
            {
                _context.ReturnBatchToPool(batch);
            }

            _completed = true;
        }

        public void Dispose()
        {
            if (!_completed) Wait();
            _fence.Dispose();
            _batches.Clear();
            _disposables.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed) await WaitAsync();
            _fence.Dispose();
            _batches.Clear();
            _disposables.Clear();
        }
    }
}