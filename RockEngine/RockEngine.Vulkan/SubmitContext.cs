using Silk.NET.Vulkan;

using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using ZLinq;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan
{
    public sealed partial class SubmitContext : IDisposable
    {
        private const int INITIAL_BATCHES_PER_POOL = 16;

        private readonly VulkanContext _context;
        private readonly VkQueue _targetQueue;
        private readonly SemaphoreSlim _submissionLock = new SemaphoreSlim(1, 1);
        private readonly int _ownerThreadId;

        // Double buffered queues for lock-free batch collection
        private readonly ConcurrentQueue<UploadBatch> _queueA = new();
        private readonly ConcurrentQueue<UploadBatch> _queueB = new();
        private ConcurrentQueue<UploadBatch> _activeQueue;
        private ConcurrentQueue<UploadBatch> _submissionQueue;
        private readonly FencePool _fencePool;
        private readonly CommandPoolPool _commandPoolPool;
        private SubmitOperation _lastSubmitOperation;

        // Thread-local command pools
        private readonly ThreadLocal<CommandPoolContext> _threadCommandContext;
        private readonly ConcurrentBag<CommandPoolContext> _allContexts = new ConcurrentBag<CommandPoolContext>();
        private readonly ConcurrentDictionary<string, CommandPoolContext> _namedContexts = new();

        // Per-flush resource tracking
        private readonly ConcurrentBag<IDisposable> _flushDisposables = new();
        private readonly ConcurrentBag<VkSemaphore> _signalSemaphores = new();
        private readonly ConcurrentDictionary<VkSemaphore, PipelineStageFlags> _waitSemaphores = new();

        // Reusable collections for submission
        private readonly List<CommandBuffer> _commandBufferList = new(64);
        private readonly List<IDisposable> _disposableList = new(64);
        private readonly List<UploadBatch> _batchList = new(64);

        private static readonly ArrayPool<CommandBuffer> _commandBufferPool = ArrayPool<CommandBuffer>.Shared;

        private readonly ConcurrentBag<(List<UploadBatch> batches, List<IDisposable> disposables)> _pendingNoFenceResources = new();

        public uint QueueFamily => _targetQueue.FamilyIndex;

        public SubmitContext(VulkanContext context, VkQueue targetQueue)
        {
            _context = context;
            _targetQueue = targetQueue;
            _activeQueue = _queueA;
            _submissionQueue = _queueB;
            _fencePool = new FencePool(context);
            _commandPoolPool = new CommandPoolPool(context, targetQueue.FamilyIndex);

            _threadCommandContext = new ThreadLocal<CommandPoolContext>(() => {
                var ctx = CreateCommandPoolContext();
                _allContexts.Add(ctx);
                return ctx;
            }, trackAllValues: true);
        }

        private CommandPoolContext CreateCommandPoolContext()
        {
            var cmdPool = _commandPoolPool.Rent();
            return new CommandPoolContext(cmdPool, this);
        }

        private CommandPoolContext CreateNamedCommandPoolContext(string name)
        {
            var cmdPool = _commandPoolPool.Rent();
            return new CommandPoolContext(cmdPool, this, name);
        }

        private UploadBatch CreateNewBatch(CommandPoolContext context, CommandBufferLevel level, CommandBufferInheritanceInfo? inheritanceInfo = null)
        {
            var commandBuffer = context.Pool.AllocateCommandBuffer(level);
            return new UploadBatch(context, new StagingManager(_context), this, commandBuffer, level, inheritanceInfo);
        }

        public UploadBatch CreateBatch(BatchCreationParams? parameters = null)
        {
            parameters ??= new BatchCreationParams();
            var context = GetOrCreateContext(parameters.Name);

            UploadBatch batch;

            // Try to get an existing batch
            if (!context.TryGetAvailableBatch(parameters.Level, out batch!))
            {
                // Create a new batch and mark it as taken
                batch = CreateNewBatch(context, parameters.Level, parameters.InheritanceInfo);
                context.OnBatchTaken(); // Increment flight counter

            }
            else
            {
                batch.InheritanceInfo = parameters.InheritanceInfo;
            }
            batch.BeginCommandBuffer();

            batch.MarkInUse();


            for (int i = 0; i < parameters.WaitSemaphores.Count; i++)
            {
                batch.AddWaitSemaphore(
                    parameters.WaitSemaphores[i],
                    i < parameters.WaitStages.Count ? parameters.WaitStages[i] : PipelineStageFlags.TopOfPipeBit
                );
            }

            foreach (var semaphore in parameters.SignalSemaphores)
            {
                batch.AddSignalSemaphore(semaphore);
            }

            return batch;
        }

        private CommandPoolContext GetOrCreateContext(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return _threadCommandContext.Value!;
            }

            return _namedContexts.GetOrAdd(name, n => {
                var ctx = CreateNamedCommandPoolContext(n);
                _allContexts.Add(ctx);
                return ctx;
            });
        }

        internal void AddSubmission(UploadBatch batch)
        {
            _activeQueue.Enqueue(batch);
        }

        internal void ReturnBatchToPool(UploadBatch batch)
        {
            batch.Context.ReturnBatch(batch);
        }

        public SubmitOperation Submit(VkFence? fence = null)
        {
            return SubmitInternal(fence);
        }


        public SubmitOperation SubmitSingle(UploadBatch batch, VkFence? fence = null)
        {
            batch.End();

            _lastSubmitOperation?.Wait();

            // Only lock during submission preparation
            //_submissionLock.Wait();
            try
            {
                // Store disposables and batches
                var disposableList = new List<IDisposable>();
                var batchList = new List<UploadBatch>();
                bool poolOwned = fence is null;
                if (poolOwned)
                {
                    fence = _fencePool.GetFence();
                    batch.AddDependency(new DeferredOperation(()=>_fencePool.ReturnFence(fence)));
                }


                disposableList.AddRange(batch.Disposables);
                batchList.Add(batch);

                Span<Semaphore> semaphores = stackalloc Semaphore[batch.SignalSemaphores.Count];
                for (int i = 0; i < batch.SignalSemaphores.Count; i++)
                {
                    semaphores[i] = batch.SignalSemaphores[i].VkObjectNative;
                }

                Span<Semaphore> waitSemaphores = stackalloc Semaphore[batch.WaitSemaphores.Count];
                Span<PipelineStageFlags> stages = stackalloc PipelineStageFlags[batch.WaitSemaphores.Count];

                int j = 0;
                foreach (var kvp in batch.WaitSemaphores)
                {
                    waitSemaphores[j] = kvp.Key.VkObjectNative;
                    stages[j] = kvp.Value;
                    j++;
                }
                var collectedSemaphores = new List<VkSemaphore>();
                collectedSemaphores.AddRange(batch.SignalSemaphores);
                collectedSemaphores.AddRange(batch.WaitSemaphores.Keys);
                fence ??= VkFence.CreateNotSignaled(_context);

                _targetQueue.Submit(
                    batch.CommandBuffer,
                    semaphores,
                    waitSemaphores,
                    stages,
                    fence
                );

                foreach (var (batches, disposables) in _pendingNoFenceResources)
                {
                    batchList.AddRange(batches);
                    disposableList.AddRange(disposables);
                }
                _pendingNoFenceResources.Clear();
                // When fence is provided, SubmitOperation will handle cleanup
                _lastSubmitOperation = new SubmitOperation(
                    this,
                    fence,
                    batchList,
                    disposableList,
                    collectedSemaphores
                );
                return _lastSubmitOperation;

            }
            finally
            {
                //_submissionLock.Release();
            }
           
        }

        private SubmitOperation SubmitInternal(VkFence? fence = null)
        {
            // Only lock during submission preparation
            //_submissionLock.Wait();
            try
            {
                _lastSubmitOperation?.Wait();
                // Swap active and submission queues
                (_submissionQueue, _activeQueue) = (_activeQueue, _submissionQueue);
                bool poolOwned = fence is null;
                if (poolOwned)
                {
                    fence = _fencePool.GetFence();
                    AddDependency(new DeferredOperation(() => _fencePool.ReturnFence(fence)));
                }
                PrepareSubmissionData();
                SubmitCommandBuffers(fence);

                foreach (var (batches, disposables) in _pendingNoFenceResources)
                {
                    _batchList.AddRange(batches);
                    _disposableList.AddRange(disposables);
                }
                _pendingNoFenceResources.Clear();
                var semaphores = new List<VkSemaphore>();
                semaphores.AddRange(_signalSemaphores);
                foreach (var item in _waitSemaphores)
                {
                    semaphores.Add(item.Key);
                }
                foreach (var batch in _batchList)
                {
                    semaphores.AddRange(batch.SignalSemaphores);
                    semaphores.AddRange(batch.WaitSemaphores.Keys);
                }
                // Create operation that will handle cleanup when fence passes
                var operation = new SubmitOperation(this, fence,
               [.. _batchList],
               [.. _disposableList],
               semaphores);
                ResetState();
                _lastSubmitOperation = operation;

                return operation;

            }
            finally
            {
                //_submissionLock.Release();
            }
        }

        private void CleanUpNoFenceResources()
        {
            foreach (var (batches, disposables) in _pendingNoFenceResources)
            {
                foreach (var item in batches)
                {
                    ReturnBatchToPool(item);
                }
                foreach (var item in disposables)
                {
                    item.Dispose();
                }
            }
            _pendingNoFenceResources.Clear();
        }

        private void PrepareSubmissionData()
        {
            _commandBufferList.Clear();
            _disposableList.Clear();
            _batchList.Clear();

            while (_submissionQueue.TryDequeue(out var batch))
            {
                _batchList.Add(batch);
                _commandBufferList.Add(batch.CommandBuffer.VkObjectNative);
                _disposableList.AddRange(batch.Disposables);
            }

            _disposableList.AddRange(_flushDisposables);
        }

        private void SubmitCommandBuffers(VkFence? fence = null)
        {
            int signalCount = _signalSemaphores.Count + _batchList.Sum(b => b.SignalSemaphores.Count);
            int waitCount = _waitSemaphores.Count + _batchList.Sum(b => b.WaitSemaphores.Count);

            var signalPool = ArrayPool<Semaphore>.Shared;
            var waitPool = ArrayPool<Semaphore>.Shared;
            var stagePool = ArrayPool<PipelineStageFlags>.Shared;

            var signalSemaphores = signalPool.Rent(signalCount);
            var waitSemaphores = waitPool.Rent(waitCount);
            var waitStages = stagePool.Rent(waitCount);

            try
            {
                // Fill signalSemaphores
                int index = 0;
                foreach (var s in _signalSemaphores)
                    signalSemaphores[index++] = s.VkObjectNative;
                foreach (var b in _batchList)
                    foreach (var s in b.SignalSemaphores)
                        signalSemaphores[index++] = s.VkObjectNative;

                // Fill waitSemaphores and waitStages
                index = 0;
                foreach (var (sem, stage) in _waitSemaphores)
                {
                    waitSemaphores[index] = sem.VkObjectNative;
                    waitStages[index] = stage;
                    index++;
                }
                foreach (var b in _batchList)
                {
                    foreach (var (sem, stage) in b.WaitSemaphores)
                    {
                        waitSemaphores[index] = sem.VkObjectNative;
                        waitStages[index] = stage;
                        index++;
                    }
                }

                _targetQueue.Submit(
                    CollectionsMarshal.AsSpan(_commandBufferList),
                    signalSemaphores.AsSpan(0, signalCount),
                    waitSemaphores.AsSpan(0, waitCount),
                    waitStages.AsSpan(0, waitCount),
                    fence
                );
            }
            finally
            {
                signalPool.Return(signalSemaphores);
                waitPool.Return(waitSemaphores);
                stagePool.Return(waitStages);
            }
        }

        /*private SubmitOperation CreateFlushOperation(VkFence? fence = null)
        {
            return new SubmitOperation(
                this,
                fence,
                [.. _batchList],
                [.. _disposableList]
            );
        }*/

        private void ResetState()
        {
            _flushDisposables.Clear();
            _signalSemaphores.Clear();
            _waitSemaphores.Clear();
        }

        public void AddDependency(IDisposable disposable) => _flushDisposables.Add(disposable);
        public void AddWaitSemaphore(VkSemaphore semaphore, PipelineStageFlags stage) => _waitSemaphores[semaphore] = stage;
        public void AddSignalSemaphore(VkSemaphore semaphore) => _signalSemaphores.Add(semaphore);


        public void Dispose()
        {
            // Dispose all command pool contexts (returns their pools to the pool)
            foreach (var context in _allContexts)
            {
                context.Dispose();
            }
            _allContexts.Clear();
            _namedContexts.Clear();

            // Dispose the pool – this will destroy any remaining command pools
            _commandPoolPool?.Dispose();
            _fencePool?.Dispose();

            GC.SuppressFinalize(this);
        }
        private sealed class CommandPoolPool
        {
            private readonly VulkanContext _context;
            private readonly uint _queueFamily;
            private readonly ConcurrentBag<VkCommandPool> _available = new();

            public CommandPoolPool(VulkanContext context, uint queueFamily)
            {
                _context = context;
                _queueFamily = queueFamily;
            }

            public VkCommandPool Rent()
            {
                // Try to get an existing pool; it's already reset from previous Return
                if (_available.TryTake(out var pool))
                    return pool;

                // Create a new pool with the required flags
                return VkCommandPool.Create(
                    _context,
                    CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit,
                    _queueFamily
                );
            }

            public void Return(VkCommandPool pool)
            {
                // Reset the pool to free all command buffers (redundant if already reset, but safe)
                pool.Reset();
                _available.Add(pool);
            }

            public void Dispose()
            {
                foreach (var pool in _available)
                    pool.Dispose();
                _available.Clear();
            }
        }
        internal sealed class CommandPoolContext : IDisposable
        {
            private readonly SubmitContext _submitContext;
            private readonly ConcurrentDictionary<CommandBufferLevel, ConcurrentQueue<UploadBatch>> _oneTimeBatches = new();
            private int _batchesInFlight; // Number of batches currently in use (not yet returned)
            public VkCommandPool Pool { get; }
            public string? Name { get; }
            private bool _disposed;

            public CommandPoolContext(VkCommandPool pool, SubmitContext submitContext, string? name = null)
            {
                Pool = pool;
                _submitContext = submitContext;
                Name = name;
            }

            /// <summary>Call when a batch is taken (either from queue or newly created).</summary>
            public void OnBatchTaken()
            {
                Interlocked.Increment(ref _batchesInFlight);
            }

            public bool TryGetAvailableBatch(CommandBufferLevel level, out UploadBatch? batch)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_oneTimeBatches.TryGetValue(level, out var queue))
                {
                    batch = null;
                    return false;
                }
                if (queue.TryDequeue(out batch))
                {
                    OnBatchTaken();
                    return true;
                }
                batch = null;
                return false;
            }

            public void ReturnBatch(UploadBatch batch)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                batch.ResetLists(); // Clear internal lists, but NOT the command buffer
                var queue = _oneTimeBatches.GetOrAdd(batch.Level, _ => new ConcurrentQueue<UploadBatch>());
                queue.Enqueue(batch);

                int inFlight = Interlocked.Decrement(ref _batchesInFlight);
                if (inFlight == 0)
                {
                    // All batches returned – reset the entire pool (batches all command buffers)
                    Pool.Reset( CommandPoolResetFlags.None);
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                // Clear any leftover batches (should be none if properly used)
                foreach (var queue in _oneTimeBatches.Values)
                {
                    while (queue.TryDequeue(out var batch))
                    {
                        // Command buffer will be freed when pool is reset
                    }
                }
                _oneTimeBatches.Clear();

                // Return the command pool to the central pool (will be reset there)
                _submitContext._commandPoolPool.Return(Pool);
            }
        }
    }
}
