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
        private readonly StagingManager _stagingManager;
        private readonly SemaphoreSlim _submissionLock = new SemaphoreSlim(1, 1);
        private readonly int _ownerThreadId;

        // Double buffered queues for lock-free batch collection
        private readonly ConcurrentQueue<UploadBatch> _queueA = new();
        private readonly ConcurrentQueue<UploadBatch> _queueB = new();
        private ConcurrentQueue<UploadBatch> _activeQueue;
        private ConcurrentQueue<UploadBatch> _submissionQueue;

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

        public StagingManager StagingManager => _stagingManager;

        public uint QueueFamily => _targetQueue.FamilyIndex;

        public SubmitContext(VulkanContext context, VkQueue targetQueue)
        {
            _context = context;
            _targetQueue = targetQueue;
            _stagingManager = new StagingManager(context, this);
            _activeQueue = _queueA;
            _submissionQueue = _queueB;

            _threadCommandContext = new ThreadLocal<CommandPoolContext>(() => {
                var ctx = CreateCommandPoolContext();
                _allContexts.Add(ctx);
                return ctx;
            }, trackAllValues: true);
        }

        private CommandPoolContext CreateCommandPoolContext()
        {
            var cmdPool = VkCommandPool.Create(
                _context,
                CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit,
                _targetQueue.FamilyIndex
            );

            return new CommandPoolContext(cmdPool);
        }

        private CommandPoolContext CreateNamedCommandPoolContext(string name)
        {
            var cmdPool = VkCommandPool.Create(
                _context,
                CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit,
                _targetQueue.FamilyIndex
            );

            return new CommandPoolContext(cmdPool, name);
        }

        private UploadBatch CreateNewBatch(CommandPoolContext context, CommandBufferLevel level, CommandBufferInheritanceInfo? inheritanceInfo = null)
        {
            var commandBuffer = context.Pool.AllocateCommandBuffer(level);
            return new UploadBatch(context, _stagingManager, this, commandBuffer, level, inheritanceInfo);
        }

        public UploadBatch CreateBatch(BatchCreationParams? parameters = null)
        {
            parameters ??= new BatchCreationParams();
            var context = GetOrCreateContext(parameters.Name);

            UploadBatch batch;


            // Versioned batch
            if (!context.TryGetAvailableBatch(parameters.Level, out batch!))
            {
                batch = CreateNewBatch(context, parameters.Level, parameters.InheritanceInfo);
            }
            else
            {
                batch.InheritanceInfo = parameters.InheritanceInfo;
            }

            batch.MarkInUse();

            batch.ResetCommandBuffer();

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

            // Only lock during submission preparation
            _submissionLock.Wait();
            try
            {
                // Store disposables and batches
                var disposableList = new List<IDisposable>();
                var batchList = new List<UploadBatch>();

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

                _targetQueue.Submit(
                    batch.CommandBuffer,
                    semaphores,
                    waitSemaphores,
                    stages,
                    fence
                );

                if (fence != null)
                {
                    foreach (var (batches, disposables) in _pendingNoFenceResources)
                    {
                        batchList.AddRange(batches);
                        disposableList.AddRange(disposables);
                    }
                    _pendingNoFenceResources.Clear();
                    // When fence is provided, SubmitOperation will handle cleanup
                    return new SubmitOperation(
                        this,
                        fence,
                        batchList,
                        disposableList
                    );
                }
                else
                {
                    // No fence - keep resources in SubmitContext for later cleanup
                    _pendingNoFenceResources.Add((batchList, disposableList));

                    // Return a completed operation that doesn't own resources
                    var operation = new SubmitOperation(this, null, [], []);
                    operation.SetCompleted(true);
                    return operation;
                }
            }
            finally
            {
                _submissionLock.Release();
            }
        }

        private SubmitOperation SubmitInternal(VkFence? fence = null)
        {
            // Only lock during submission preparation
            _submissionLock.Wait();
            try
            {
                // Swap active and submission queues
                (_submissionQueue, _activeQueue) = (_activeQueue, _submissionQueue);

               
                PrepareSubmissionData();
                SubmitCommandBuffers(fence);
               

                if (fence != null)
                {
                    foreach (var (batches, disposables) in _pendingNoFenceResources)
                    {
                        _batchList.AddRange(batches);
                        _disposableList.AddRange(disposables);
                    }
                    _pendingNoFenceResources.Clear();
                    // Create operation that will handle cleanup when fence passes
                    var operation = CreateFlushOperation(fence);
                    ResetState();
                    StagingManager.Reset();

                    return operation;
                }
                else
                {
                    // No fence - keep resources in SubmitContext
                    var batchesCopy = new List<UploadBatch>(_batchList);
                    var disposablesCopy = new List<IDisposable>(_disposableList);

                    // Store for later cleanup
                    _pendingNoFenceResources.Add((batchesCopy, disposablesCopy));

                    // Reset state (transferred ownership to pending resources)
                    ResetState();

                    // Return completed operation that doesn't own resources
                    var operation = new SubmitOperation(this, null, [], []);
                    operation.SetCompleted(true);
                    return operation;
                }
            }
            finally
            {
                _submissionLock.Release();
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

        private SubmitOperation CreateFlushOperation(VkFence? fence = null)
        {
            return new SubmitOperation(
                this,
                fence,
                [.. _batchList],
                [.. _disposableList]
            );
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
            _stagingManager.Dispose();

            foreach (var context in _allContexts)
            {
                context.Pool.Dispose();
            }
            _allContexts.Clear();
            _namedContexts.Clear();

            GC.SuppressFinalize(this);
        }

        internal sealed class CommandPoolContext
        {
            private readonly ConcurrentDictionary<CommandBufferLevel, ConcurrentQueue<UploadBatch>> _oneTimeBatches = new ConcurrentDictionary<CommandBufferLevel, ConcurrentQueue<UploadBatch>>();

            public VkCommandPool Pool { get; }
            public string? Name { get; }

            public CommandPoolContext(VkCommandPool pool, string? name = null)
            {
                Pool = pool;
                Name = name;
            }

            public bool TryGetAvailableBatch(CommandBufferLevel level, out UploadBatch? batch)
            {
                if (!_oneTimeBatches.TryGetValue(level, out var batchQueue))
                {
                    batch = null;
                    return false;
                }
                else
                {
                    return batchQueue.TryDequeue(out batch);
                }
            }

            public void ReturnBatch(UploadBatch batch)
            {
                batch.ResetLists();
                var queue = _oneTimeBatches.GetOrAdd(batch.Level, _ => new ConcurrentQueue<UploadBatch>());
                batch.ResetLists();
                queue.Enqueue(batch);
            }
        }
    }
}