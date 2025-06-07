using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan
{
    /// <summary>
    /// Manages submission of GPU commands and resource lifecycle
    /// </summary>
    public sealed class SubmitContext : IDisposable
    {
        private const int INITIAL_BATCHES_PER_POOL = 16;

        private readonly VulkanContext _context;
        private readonly VkQueue _targetQueue;
        private readonly StagingManager _stagingManager;
        private readonly VkFence _fence;

        // Submission tracking
        private readonly ConcurrentQueue<UploadBatch> _pendingSubmissions = new();
        private readonly ConcurrentBag<IDisposable> _flushDisposables = new();
        private readonly ConcurrentBag<VkSemaphore> _signalSemaphores = new();
        private readonly ConcurrentDictionary<VkSemaphore, PipelineStageFlags> _waitSemaphores = new();

        // Command buffer management
        private readonly ThreadLocal<(VkCommandPool Pool, Stack<UploadBatch> Batches)> _threadCommandPools;

        // Reusable collections for submission
        private readonly List<CommandBuffer> _commandBufferList = new();
        private readonly List<IDisposable> _disposableList = new();
        private readonly List<UploadBatch> _batchList = new();

        // Cross-thread batch returns
        private readonly List<UploadBatch> _crossThreadReturns = new();
        private readonly Lock _crossThreadLock = new();

        public StagingManager StagingManager => _stagingManager;

        public SubmitContext(VulkanContext context, VkQueue targetQueue)
        {
            _context = context;
            _targetQueue = targetQueue;
            _stagingManager = new StagingManager(context, this);
            _fence = VkFence.CreateNotSignaled(context);

            _threadCommandPools = new ThreadLocal<(VkCommandPool, Stack<UploadBatch>)>(
                () => CreateThreadCommandPool(),
                trackAllValues: true
            );
        }

        /// <summary>
        /// Creates a command pool and preallocates batches for the current thread
        /// </summary>
        private (VkCommandPool, Stack<UploadBatch>) CreateThreadCommandPool()
        {
            var cmdPool = VkCommandPool.Create(
                _context,
                CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit,
                _targetQueue.FamilyIndex
            );

            var batches = new Stack<UploadBatch>(INITIAL_BATCHES_PER_POOL);

            // Preallocate batches
            for (int i = 0; i < INITIAL_BATCHES_PER_POOL; i++)
            {
                batches.Push(CreateNewBatch(cmdPool));
            }

            return (cmdPool, batches);
        }

        /// <summary>
        /// Creates a new command batch using the specified command pool
        /// </summary>
        private UploadBatch CreateNewBatch(VkCommandPool pool)
        {
            var commandBuffer = pool.AllocateCommandBuffer(CommandBufferLevel.Primary);
            return new UploadBatch(_stagingManager, this, commandBuffer);
        }

        /// <summary>
        /// Gets a command batch from the current thread's pool
        /// </summary>
        public UploadBatch CreateBatch()
        {
            var (pool, batches) = _threadCommandPools.Value;
            UploadBatch batch;

            if (batches.Count > 0)
            {
                batch = batches.Pop();
            }
            else
            {
                batch = CreateNewBatch(pool);
            }

            batch.MarkInUse();
            batch.ResetForPool();
            return batch;
        }

        /// <summary>
        /// Adds a batch to the submission queue
        /// </summary>
        internal void AddSubmission(UploadBatch batch)
        {
            _pendingSubmissions.Enqueue(batch);

            // Collect semaphores for global submission
            foreach (var semaphore in batch.SignalSemaphores)
            {
                _signalSemaphores.Add(semaphore);
            }

            foreach (var (semaphore, stage) in batch.WaitSemaphores)
            {
                _waitSemaphores.TryAdd(semaphore, stage);
            }
        }

        /// <summary>
        /// Returns a batch to its originating pool for reuse
        /// </summary>
        internal void ReturnBatchToPool(UploadBatch batch)
        {
            var currentThreadPool = _threadCommandPools.Value.Pool;

            if (batch.CommandBuffer.CommandPool == currentThreadPool)
            {
                ReturnToLocalPool(batch);
            }
            else
            {
                ReturnToForeignPool(batch);
            }
        }

        private void ReturnToLocalPool(UploadBatch batch)
        {
            batch.ResetForPool();
            _threadCommandPools.Value.Batches.Push(batch);
        }

        private void ReturnToForeignPool(UploadBatch batch)
        {
            lock (_crossThreadLock)
            {
                _crossThreadReturns.Add(batch);
            }
        }

        /// <summary>
        /// Processes batches returned from different threads
        /// </summary>
        private void ProcessCrossThreadReturns()
        {
            lock (_crossThreadLock)
            {
                foreach (var batch in _crossThreadReturns)
                {
                    batch.ResetForPool();

                    // Find the pool that created this batch
                    foreach (var (pool, batches) in _threadCommandPools.Values)
                    {
                        if (pool == batch.CommandBuffer.CommandPool)
                        {
                            batches.Push(batch);
                            break;
                        }
                    }
                }

                _crossThreadReturns.Clear();
            }
        }

        /// <summary>
        /// Submits all pending batches synchronously
        /// </summary>
        public void Flush()
        {
            if (_pendingSubmissions.IsEmpty) return;

            ProcessCrossThreadReturns();
            _fence.Reset();

            PrepareSubmissionData();
            SubmitCommandBuffers(_fence);
            CreateFlushOperation(_fence).Wait();

            ResetState();
        }

        /// <summary>
        /// Submits all pending batches asynchronously
        /// </summary>
        public FlushOperation FlushAsync(VkFence fence)
        {
            if (_pendingSubmissions.IsEmpty)
            {
                return new FlushOperation(this, fence, [], []);
            }

            ProcessCrossThreadReturns();
            fence.Reset();

            PrepareSubmissionData();
            SubmitCommandBuffers(fence);
            var operation = CreateFlushOperation(fence);

            ResetState();
            return operation;
        }

        /// <summary>
        /// Gathers all submission data into reusable lists
        /// </summary>
        private void PrepareSubmissionData()
        {
            _commandBufferList.Clear();
            _disposableList.Clear();
            _batchList.Clear();

            // Collect all pending batches
            while (_pendingSubmissions.TryDequeue(out var batch))
            {
                _batchList.Add(batch);
                _commandBufferList.Add(batch.CommandBuffer);
                _disposableList.AddRange(batch.Disposables);
            }

            // Add global dependencies
            _disposableList.AddRange(_flushDisposables);
        }

        /// <summary>
        /// Submits command buffers to the GPU queue
        /// </summary>
        private void SubmitCommandBuffers(VkFence fence)
        {
            _targetQueue.Submit(
                CollectionsMarshal.AsSpan(_commandBufferList),
                _signalSemaphores.Select(s => s.VkObjectNative).ToArray(),
                _waitSemaphores.Keys.Select(s => s.VkObjectNative).ToArray(),
                _waitSemaphores.Values.ToArray(),
                fence
            );
        }

        /// <summary>
        /// Creates a flush operation to track submission completion
        /// </summary>
        private FlushOperation CreateFlushOperation(VkFence fence)
        {
            return new FlushOperation(
                this,
                fence,
                new List<UploadBatch>(_batchList),
                new List<IDisposable>(_disposableList)
            );
        }

        /// <summary>
        /// Resets context state for the next submission
        /// </summary>
        private void ResetState()
        {
            _flushDisposables.Clear();
            _signalSemaphores.Clear();
            _waitSemaphores.Clear();
            _stagingManager.Reset();
        }

        public void AddDependency(IDisposable disposable)
            => _flushDisposables.Add(disposable);

        public void AddWaitSemaphore(VkSemaphore semaphore, PipelineStageFlags stage)
            => _waitSemaphores[semaphore] = stage;

        public void AddSignalSemaphore(VkSemaphore semaphore)
            => _signalSemaphores.Add(semaphore);

        public void Dispose()
        {
            _stagingManager.Dispose();
            _fence.Dispose();


            GC.SuppressFinalize(this);
        }
    }
}