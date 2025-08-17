using Silk.NET.Vulkan;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using ZLinq;

using Semaphore = Silk.NET.Vulkan.Semaphore;

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
        private readonly Lock _submissionLock = new Lock();

        // Double buffered queues for lock-free batch collection
        private readonly ConcurrentQueue<UploadBatch> _queueA = new();
        private readonly ConcurrentQueue<UploadBatch> _queueB = new();
        private ConcurrentQueue<UploadBatch> _activeQueue = new();
        private ConcurrentQueue<UploadBatch> _submissionQueue = new();

        // Async-local command buffer management
        private readonly AsyncLocal<CommandPoolContext> _asyncCommandContext = new AsyncLocal<CommandPoolContext>();
        private readonly ConcurrentDictionary<CommandPoolContext, object> _allContexts = new ConcurrentDictionary<CommandPoolContext, object>();
        private readonly ConcurrentQueue<CommandPoolContext> _contextPool = new ConcurrentQueue<CommandPoolContext>();

        // Per-flush resource tracking
        private readonly ConcurrentBag<Action> _flushDisposables = new();
        private readonly ConcurrentBag<VkSemaphore> _signalSemaphores = new();
        private readonly ConcurrentDictionary<VkSemaphore, PipelineStageFlags> _waitSemaphores = new();

        // Reusable collections for submission
        private readonly List<CommandBuffer> _commandBufferList = new(64);
        private readonly List<Action> _disposableList = new(64);
        private readonly List<UploadBatch> _batchList = new(64);


        // Cross-thread batch returns
        private readonly List<UploadBatch> _crossThreadReturns = new();
        private readonly Lock _crossThreadLock = new Lock();

        private static readonly ArrayPool<CommandBuffer> _commandBufferPool = ArrayPool<CommandBuffer>.Shared;

        public StagingManager StagingManager => _stagingManager;

        public SubmitContext(VulkanContext context, VkQueue targetQueue)
        {
            _context = context;
            _targetQueue = targetQueue;
            _stagingManager = new StagingManager(context, this);
            _activeQueue = _queueA;
            _submissionQueue = _queueB;

            // Pre-create some contexts to reuse
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                _contextPool.Enqueue(CreateCommandPoolContext());
            }
        }

        private CommandPoolContext CreateCommandPoolContext()
        {
            var cmdPool = VkCommandPool.Create(
                _context,
                CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit,
                _targetQueue.FamilyIndex
            );

            var batches = new Stack<UploadBatch>(INITIAL_BATCHES_PER_POOL);
            for (int i = 0; i < INITIAL_BATCHES_PER_POOL; i++)
            {
                batches.Push(CreateNewBatch(cmdPool));
            }

            var context = new CommandPoolContext(cmdPool, batches);
            _allContexts.TryAdd(context, null);
            return context;
        }

        private UploadBatch CreateNewBatch(VkCommandPool pool)
        {
            var commandBuffer = pool.AllocateCommandBuffer(CommandBufferLevel.Primary);
            return new UploadBatch(_stagingManager, this, commandBuffer);
        }

        public UploadBatch CreateBatch()
        {
            // Try to get an existing context for this async flow
            var context = _asyncCommandContext.Value;

            // If no context exists for this async flow, try to reuse one
            if (context == null)
            {
                if (!_contextPool.TryDequeue(out context))
                {
                    context = CreateCommandPoolContext();
                }
                _asyncCommandContext.Value = context;
            }

            // Get a batch from the context
            if (!context.Batches.TryPop(out var batch))
            {
                batch = CreateNewBatch(context.Pool);
            }

            batch.MarkInUse();
            batch.ResetForPool();
            return batch;
        }

        internal void AddSubmission(UploadBatch batch)
        {
            _activeQueue.Enqueue(batch);
            ReturnContextToPool();
        }

        internal void ReturnBatchToPool(UploadBatch batch)
        {
            var currentContext = _asyncCommandContext.Value;
            if (currentContext != null && batch.CommandBuffer.CommandPool == currentContext.Pool)
            {
                ReturnToLocalPool(batch, currentContext);
            }
            else
            {
                ReturnToForeignPool(batch);
            }
        }

        private void ReturnToLocalPool(UploadBatch batch, CommandPoolContext context)
        {
            batch.ResetForPool();
            context.Batches.Push(batch);
        }

        private void ReturnToForeignPool(UploadBatch batch)
        {
            lock (_crossThreadLock)
            {
                _crossThreadReturns.Add(batch);
            }
        }

        private void ProcessCrossThreadReturns()
        {
            lock (_crossThreadLock)
            {
                foreach (var batch in _crossThreadReturns)
                {
                    batch.ResetForPool();

                    // Find the context that owns this batch's command pool
                    foreach (var context in _allContexts.Keys)
                    {
                        if (context.Pool == batch.CommandBuffer.CommandPool)
                        {
                            context.Batches.Push(batch);
                            break;
                        }
                    }
                }
                _crossThreadReturns.Clear();
            }
        }

        public void ReturnContextToPool()
        {
            var context = _asyncCommandContext.Value;
            if (context != null)
            {
                _asyncCommandContext.Value = null;
                _contextPool.Enqueue(context);
            }
        }


        public FlushOperation Flush(VkFence fence) => FlushInternal(fence);
        public FlushOperation FlushAsync(VkFence fence) => FlushInternal(fence);
        public FlushOperation FlushSingle(UploadBatch batch, VkFence fence)
        {
            batch.End();
            lock (_submissionLock)
            {
                ProcessCrossThreadReturns();

                // Prepare disposables
                _disposableList.Clear();
                _disposableList.AddRange(batch.Disposables);

                // Prepare semaphores using reusable lists
                Span<Semaphore> semaphores = stackalloc Semaphore[batch.SignalSemaphores.Count];
                for (int i = 0; i < batch.SignalSemaphores.Count; i++)
                {
                    semaphores[i] = batch.SignalSemaphores[i].VkObjectNative;
                }

                Span<Semaphore> waitSemaphores = stackalloc Semaphore[batch.SignalSemaphores.Count];
                Span<PipelineStageFlags> stages = stackalloc PipelineStageFlags[batch.SignalSemaphores.Count];

                int j = 0;
                foreach (var kvp in batch.WaitSemaphores)
                {
                    waitSemaphores[j] = kvp.Key.VkObjectNative;
                    stages[j] = kvp.Value;
                    j++;
                }

                // Rent array for command buffers
                var commandBuffers = _commandBufferPool.Rent(1);

                _targetQueue.Submit(
                    batch.CommandBuffer,
                    semaphores,
                    waitSemaphores,
                    stages,
                    fence
                );

                _commandBufferPool.Return(commandBuffers);

                return new FlushOperation(
                    this,
                    fence,
                    new List<UploadBatch> { batch },
                    new List<Action>(_disposableList)
                );
            }
        }

        private FlushOperation FlushInternal(VkFence fence)
        {
            lock (_submissionLock)
            {
                ProcessCrossThreadReturns();

                // Swap active and submission queues
                (_submissionQueue, _activeQueue) = (_activeQueue, _submissionQueue);

                // Check for empty submission
                if (_submissionQueue.IsEmpty &&
                    _flushDisposables.IsEmpty &&
                    _signalSemaphores.IsEmpty &&
                    _waitSemaphores.IsEmpty)
                {
                    // Сигнализируем забор вручную для пустых операций
                    fence.Signal(_targetQueue);
                    var flushOp = new FlushOperation(this, fence, [], []);
                    flushOp.SetCompleted(true);
                    return flushOp;
                }

                //fence.Reset();
                PrepareSubmissionData();
                SubmitCommandBuffers(fence);
                var operation = CreateFlushOperation(fence);
                ResetState();
                StagingManager.Reset();
                return operation;
            }
        }

        private void PrepareSubmissionData()
        {
            _commandBufferList.Clear();
            _disposableList.Clear();
            _batchList.Clear();

            // Collect batches from submission queue
            while (_submissionQueue.TryDequeue(out var batch))
            {
                _batchList.Add(batch);
                _commandBufferList.Add(batch.CommandBuffer);
                _disposableList.AddRange(batch.Disposables);
            }

            // Add global resources
            _disposableList.AddRange(_flushDisposables);
        }

        private void SubmitCommandBuffers(VkFence fence)
        {
            // Build semaphore arrays
            var signalSemaphores = _signalSemaphores
                .AsValueEnumerable()
                .Union(_batchList.SelectMany(b => b.SignalSemaphores))
                .Select(s => s.VkObjectNative)
                .ToArray();

            var waitSemaphores = _waitSemaphores
                .AsValueEnumerable()
                .Concat(_batchList.SelectMany(b => b.WaitSemaphores))
                .Select(kvp => kvp.Key.VkObjectNative)
                .ToArray();

            var waitStages = _waitSemaphores
                .AsValueEnumerable()
                .Concat(_batchList.SelectMany(b => b.WaitSemaphores))
                .Select(kvp => kvp.Value)
                .ToArray();

            _targetQueue.Submit(
                CollectionsMarshal.AsSpan(_commandBufferList),
                signalSemaphores,
                waitSemaphores,
                waitStages,
                fence
            );
        }

        private FlushOperation CreateFlushOperation(VkFence fence)
        {
            return new FlushOperation(
                this,
                fence,
                new List<UploadBatch>(_batchList),
                new List<Action>(_disposableList)
            );
        }

        private void ResetState()
        {
            _flushDisposables.Clear();
            _signalSemaphores.Clear();
            _waitSemaphores.Clear();
            _stagingManager.Reset();
        }

        public void AddDependency(IDisposable disposable) => _flushDisposables.Add(disposable.Dispose);
        public void AddDependency(Action disposable) => _flushDisposables.Add(disposable);
        public void AddWaitSemaphore(VkSemaphore semaphore, PipelineStageFlags stage) => _waitSemaphores[semaphore] = stage;
        public void AddSignalSemaphore(VkSemaphore semaphore) => _signalSemaphores.Add(semaphore);

        public void Dispose()
        {
            _stagingManager.Dispose();

            // Dispose all command pools
            foreach (var context in _allContexts.Keys)
            {
                context.Pool.Dispose();
            }
            _allContexts.Clear();
            _contextPool.Clear();

            GC.SuppressFinalize(this);
        }

        private sealed class CommandPoolContext
        {
            public VkCommandPool Pool { get; }
            public Stack<UploadBatch> Batches { get; }

            public CommandPoolContext(VkCommandPool pool, Stack<UploadBatch> batches)
            {
                Pool = pool;
                Batches = batches;
            }
        }
    }
}