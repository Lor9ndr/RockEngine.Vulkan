
using Silk.NET.GLFW;
using Silk.NET.Vulkan;

using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
        private readonly Lock _submissionLock = new Lock();
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

        public StagingManager StagingManager => _stagingManager;

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
                // Don't change the version - it should already be correct
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

        public FlushOperation Flush(VkFence fence)
        {
            return FlushInternal(fence);
        }

        public FlushOperation FlushAsync(VkFence fence)
        {
            return FlushInternal(fence);
        }

        public FlushOperation FlushSingle(UploadBatch batch, VkFence fence)
        {
            batch.End();
            lock (_submissionLock)
            {
                _disposableList.Clear();
                _disposableList.AddRange(batch.Disposables);

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

                return new FlushOperation(
                    this,
                    fence,
                    new List<UploadBatch> { batch },
                    new List<IDisposable>(_disposableList)
                );
            }
        }

        private FlushOperation FlushInternal(VkFence fence)
        {
            lock (_submissionLock)
            {
                // Swap active and submission queues
                (_submissionQueue, _activeQueue) = (_activeQueue, _submissionQueue);

                if (_submissionQueue.IsEmpty &&
                    _flushDisposables.IsEmpty &&
                    _signalSemaphores.IsEmpty &&
                    _waitSemaphores.IsEmpty)
                {
                    fence.Signal(_targetQueue);
                    var flushOp = new FlushOperation(this, fence, [], []);
                    flushOp.SetCompleted(true);
                    return flushOp;
                }

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

            while (_submissionQueue.TryDequeue(out var batch))
            {
                _batchList.Add(batch);
                _commandBufferList.Add(batch.CommandBuffer.VkObjectNative);
                _disposableList.AddRange(batch.Disposables);
            }

            _disposableList.AddRange(_flushDisposables);
        }

        private void SubmitCommandBuffers(VkFence fence)
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

        private FlushOperation CreateFlushOperation(VkFence fence)
        {
            return new FlushOperation(
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
                if(!_oneTimeBatches.TryGetValue(level, out var batchQueue))
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