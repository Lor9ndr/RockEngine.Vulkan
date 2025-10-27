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

        public void Wait()
        {
            if (_completed)
            {
                return;
            }

            if (_fence is not null && !Fence.IsDisposed)
            {
                _fence?.Wait();
            }
            Complete();
        }

        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            if (_completed)
            {
                return;
            }

            if (_fence is not null && !Fence.IsDisposed)
            {
                await _fence.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            Complete();
        }

        public TaskAwaiter GetAwaiter() => WaitAsync().GetAwaiter();

        private void Complete()
        {
            if (_completed)
            {
                return;
            }

            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }

            foreach (var batch in _batches)
            {
                _context.ReturnBatchToPool(batch);
            }

            _completed = true;
        }

        internal void SetCompleted(bool completed) => _completed = completed;

        public void Dispose()
        {
            Wait();
            _batches.Clear();
            _disposables.Clear();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                await WaitAsync();
            }

            _batches.Clear();
            _disposables.Clear();

            GC.SuppressFinalize(this);
        }

        ~FlushOperation()
        {
            Dispose();
        }
    }
}