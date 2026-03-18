using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using ZLinq;
using static RockEngine.Vulkan.SubmitContext.CommandPoolContext;
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

        private readonly bool _useIndividualResets;

        public uint QueueFamily => _targetQueue.FamilyIndex;

        public SubmitContext(VulkanContext context, VkQueue targetQueue, bool useIndividualResets = false)
        {
            _context = context;
            _targetQueue = targetQueue;
            _activeQueue = _queueA;
            _submissionQueue = _queueB;
            _fencePool = new FencePool(context);
            _commandPoolPool = new CommandPoolPool(context, targetQueue.FamilyIndex, useIndividualResets);

            _threadCommandContext = new ThreadLocal<CommandPoolContext>(() =>
            {
                var ctx = CreateCommandPoolContext();
                _allContexts.Add(ctx);
                return ctx;
            }, trackAllValues: true);
            _useIndividualResets = useIndividualResets;
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
            return new UploadBatch(context,  this, commandBuffer, level, inheritanceInfo);
        }

        public UploadBatch CreateBatch(BatchCreationParams? parameters = null)
        {
            parameters ??= new BatchCreationParams();
            var context = GetOrCreateContext(parameters.Name);


            // Try to get an existing batch
            if (!context.TryGetAvailableBatch(parameters.Level, out var batch, out var ownerSeg))
            {
                var newPool = _commandPoolPool.Rent();
                ownerSeg = new PoolSegment(newPool);
                batch = CreateNewBatch(context, parameters.Level, parameters.InheritanceInfo);
                batch._ownerSegment = ownerSeg;
                context.OnBatchTaken(ownerSeg); 
            }
            else
            {
                batch!.InheritanceInfo = parameters.InheritanceInfo;
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
            private readonly bool _useIndividualResets;
            private readonly ConcurrentBag<VkCommandPool> _available = new();

            public CommandPoolPool(VulkanContext context, uint queueFamily, bool useIndividualResets)
            {
                _context = context;
                _queueFamily = queueFamily;
                _useIndividualResets = useIndividualResets;
            }

            public VkCommandPool Rent()
            {
                if (_available.TryTake(out var pool))
                    return pool;

                var flags = CommandPoolCreateFlags.TransientBit;
                if (_useIndividualResets)
                    flags |= CommandPoolCreateFlags.ResetCommandBufferBit;

                return VkCommandPool.Create(_context, flags, _queueFamily);
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
            internal class PoolSegment
            {
                public VkCommandPool Pool { get; }
                public ConcurrentDictionary<CommandBufferLevel, ConcurrentQueue<UploadBatch>> FreeBatches { get; } = new();
                public ConcurrentDictionary<CommandBufferLevel, ConcurrentQueue<UploadBatch>> PendingBatches { get; } = new();
                public int InFlight;  // Interlocked used

                public PoolSegment(VkCommandPool pool) => Pool = pool;
            }
            private readonly SubmitContext _submitContext;
            private readonly bool _useIndividualResets;
            private readonly List<PoolSegment> _segments = new();
            private readonly object _segmentLock = new(); 
            private readonly ConcurrentQueue<StagingManager> _stagingManagerPool = new();
            public VkCommandPool Pool { get; }
            public string? Name { get; }
            private bool _disposed;

            public CommandPoolContext(VkCommandPool pool, SubmitContext submitContext, string? name = null)
            {
                Pool = pool;
                _submitContext = submitContext;
                _useIndividualResets = submitContext._useIndividualResets;
                Name = name;
            }

            public void OnBatchTaken(PoolSegment ownerSeg)
            {
                Interlocked.Increment(ref ownerSeg.InFlight);
            }

            public bool TryGetAvailableBatch(CommandBufferLevel level, out UploadBatch? batch, out PoolSegment? ownerSegment)
            {
                lock (_segmentLock)
                {
                    // First, try to find a free batch in any segment
                    foreach (var seg in _segments)
                    {
                        var freeQueue = seg.FreeBatches.GetOrAdd(level, _ => new ConcurrentQueue<UploadBatch>());
                        if (freeQueue.TryDequeue(out batch))
                        {
                            Interlocked.Increment(ref seg.InFlight);
                            ownerSegment = seg;
                            return true;
                        }
                    }

                    // No free batches: look for a segment with pending batches and zero in-flight
                    foreach (var seg in _segments)
                    {
                        if (seg.InFlight == 0)
                        {
                            var pendingQueue = seg.PendingBatches.GetOrAdd(level, _ => new ConcurrentQueue<UploadBatch>());
                            if (!pendingQueue.IsEmpty)
                            {
                                // Reset the pool, move all pending to free
                                seg.Pool.Reset(CommandPoolResetFlags.ReleaseResourcesBit);
                                foreach (var kv in seg.PendingBatches)
                                {
                                    var targetFree = seg.FreeBatches.GetOrAdd(kv.Key, _ => new ConcurrentQueue<UploadBatch>());
                                    while (kv.Value.TryDequeue(out var b))
                                        targetFree.Enqueue(b);
                                }
                                // Now try again (should succeed)
                                var freeNow = seg.FreeBatches.GetOrAdd(level, _ => new ConcurrentQueue<UploadBatch>());
                                if (freeNow.TryDequeue(out batch))
                                {
                                    Interlocked.Increment(ref seg.InFlight);
                                    ownerSegment = seg;
                                    return true;
                                }
                            }
                        }
                    }

                    // No reusable segment – create a new one
                    var newPool = _submitContext._commandPoolPool.Rent();
                    var newSeg = new PoolSegment(newPool);
                    _segments.Add(newSeg);
                    // Allocate a fresh command buffer and batch
                    var cmdBuffer = newPool.AllocateCommandBuffer(level);
                    batch = new UploadBatch(this, _submitContext, cmdBuffer, level)
                    {
                        _ownerSegment = newSeg
                    };
                    Interlocked.Increment(ref newSeg.InFlight);
                    ownerSegment = newSeg;
                    return true;
                }
            }
            public StagingManager RentStagingManager()
            {
                if (_stagingManagerPool.TryDequeue(out var manager))
                {
                    return manager;
                }
                // Create a new one if none available
                return new StagingManager(_submitContext._context);
            }
            public void ReturnStagingManager(StagingManager manager)
            {
                manager.Reset(); // Prepare for reuse
                _stagingManagerPool.Enqueue(manager);
            }


            public void ReturnBatch(UploadBatch batch)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var seg = batch._ownerSegment ?? throw new InvalidOperationException("Batch has no owner segment");
                batch.ResetLists();

                if (_useIndividualResets)
                {
                    batch.CommandBuffer.Reset(CommandBufferResetFlags.ReleaseResourcesBit);
                    var freeQueue = seg.FreeBatches.GetOrAdd(batch.Level, _ => new ConcurrentQueue<UploadBatch>());
                    freeQueue.Enqueue(batch);
                    Interlocked.Decrement(ref seg.InFlight);
                }
                else
                {
                    var pendingQueue = seg.PendingBatches.GetOrAdd(batch.Level, _ => new ConcurrentQueue<UploadBatch>());
                    pendingQueue.Enqueue(batch);
                    int inFlight = Interlocked.Decrement(ref seg.InFlight);

                    if (inFlight == 0)
                    {
                        // All batches of this segment are returned – reset the pool and move pending to free
                        seg.Pool.Reset(CommandPoolResetFlags.ReleaseResourcesBit);
                        foreach (var kv in seg.PendingBatches)
                        {
                            var freeQueue = seg.FreeBatches.GetOrAdd(kv.Key, _ => new ConcurrentQueue<UploadBatch>());
                            while (kv.Value.TryDequeue(out var b))
                                freeQueue.Enqueue(b);
                        }
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                // Discard any batches – their command buffers will be freed when the pool is destroyed.
                lock (_segmentLock)
                {
                    foreach (var seg in _segments)
                    {
                        _submitContext._commandPoolPool.Return(seg.Pool);
                    }
                    _segments.Clear();
                }
                while (_stagingManagerPool.TryDequeue(out var manager))
                {
                    manager.Dispose();
                }

                _submitContext._commandPoolPool.Return(Pool);
            }
        }
    }
}
