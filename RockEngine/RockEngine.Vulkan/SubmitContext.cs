using RockEngine.Core.Rendering.Managers;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan
{
    public sealed class SubmitContext : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly StagingManager _stagingManager;
        private readonly ThreadLocal<(VkCommandPool Pool, Stack<UploadBatch> BatchPool)> _perThreadResources;
        private readonly VkQueue _targetQueue;
        private readonly ConcurrentQueue<CommandBuffer> _pendingSubmissions = new();
        private readonly VkFence _fence;
        private readonly ConcurrentBag<UploadBatch> _activeBatches = new();

        private readonly ConcurrentBag<IDisposable> _flushDisposables = new();
        private readonly Lock _semaphoreLock = new Lock();
        private readonly List<VkSemaphore> _waitSemaphores = new();
        private readonly List<PipelineStageFlags> _waitStages = new();
        private readonly List<VkSemaphore> _signalSemaphores = new();

        [ThreadStatic]
        private static List<CommandBuffer> _reusableSubmissionList;

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public StagingManager StagingManager => _stagingManager;

        public SubmitContext(VulkanContext context, VkQueue targetQueue)
        {
            _context = context;
            _stagingManager = new StagingManager(context);
            _fence = VkFence.CreateNotSignaled(context);
            _targetQueue = targetQueue;

            _perThreadResources = new ThreadLocal<(VkCommandPool, Stack<UploadBatch>)>(
            () => {
                var pool = VkCommandPool.Create(
                    context,
                    CommandPoolCreateFlags.TransientBit |
                    CommandPoolCreateFlags.ResetCommandBufferBit,
                    _targetQueue.FamilyIndex);
                return (pool, new Stack<UploadBatch>(4));
            },
            trackAllValues: true
            );
        }


        public UploadBatch CreateBatch()
        {
            var (pool, batchPool) = _perThreadResources.Value;

            if (batchPool.TryPop(out var batch))
            {
                batch.Reset();
                _activeBatches.Add(batch); // Track active batch
                return batch;
            }

            var newBatch = new UploadBatch(_context, StagingManager, pool, this);
            _activeBatches.Add(newBatch); // Track new batch
            return newBatch;
        }

        public void AddSubmission(CommandBuffer commandBuffer, IDisposable[]? dependencies = null)
        {
            _pendingSubmissions.Enqueue(commandBuffer);
            if (dependencies != null)
            {
                foreach (var dependency in dependencies)
                {
                    _flushDisposables.Add(dependency);
                }
            }
        }

        public async Task FlushAsync(VkFence fence)
        {
            if (_pendingSubmissions.IsEmpty) return;

            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
               /* var usedBuffers = _stagingManager.GetUsedBuffers();
                foreach (var buffer in usedBuffers)
                {
                    _flushDisposables.Add(buffer);
                }
                _stagingManager.ClearUsedBuffers();*/

                var submissions = GetSubmissionList();
                while (_pendingSubmissions.TryDequeue(out var cmd))
                {
                    submissions.Add(cmd);
                }

                fence.Reset();

                // Convert to arrays to handle empty cases properly
                var signalSemaphores = _signalSemaphores.Select(s => s.VkObjectNative).ToArray();
                var waitSemaphores = _waitSemaphores.Select(s => s.VkObjectNative).ToArray();

                _targetQueue.Submit(
                    CollectionsMarshal.AsSpan(submissions),
                    signalSemaphores,
                    waitSemaphores,
                    _waitStages.ToArray(),
                    fence
                );

                Reset();
                ReturnSubmissionList(submissions);
            }
            finally
            {
                _semaphore.Release();
            }


        }


        public void Flush()
        {
            if (_pendingSubmissions.IsEmpty) return;
            _semaphore.Wait();
            try
            {
             /*   var usedBuffers = _stagingManager.GetUsedBuffers();
                foreach (var buffer in usedBuffers)
                {
                    _flushDisposables.Add(buffer);
                }*/
                // Reuse thread-static list to avoid allocations
                var submissions = GetSubmissionList();

                while (_pendingSubmissions.TryDequeue(out var cmd))
                {
                    submissions.Add(cmd);
                }

                _fence.Reset();

                // Use span to avoid array allocation
                var commands = CollectionsMarshal.AsSpan(submissions);
                var signalSemaphores = CollectionsMarshal.AsSpan(_signalSemaphores.Select(s => s.VkObjectNative).ToList());
                var waitSemaphores = CollectionsMarshal.AsSpan(_waitSemaphores.Select(s => s.VkObjectNative).ToList());


                _targetQueue.Submit(commands, signalSemaphores, waitSemaphores, _waitStages.ToArray(), _fence);

                _fence.Wait();

                Reset();

                ReturnSubmissionList(submissions);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void Reset()
        {
            // Return batches to pool and reset resources
            foreach (var (pool, batchPool) in _perThreadResources.Values)
            {
                pool.Reset();
                foreach (var batch in _activeBatches)
                {
                    batch.Reset(); // Reset command buffer
                    batchPool.Push(batch); // Return to pool
                }
            }
            _signalSemaphores.Clear();
            _waitSemaphores.Clear();
            _waitStages.Clear();
            foreach (var item in _flushDisposables)
            {
                item.Dispose();
            }
            _flushDisposables.Clear();
            _activeBatches.Clear(); // Clear for next frame
            StagingManager.Reset();

        }

        public void AddWaitSemaphore(VkSemaphore semaphore, PipelineStageFlags stage)
        {
            using (_semaphoreLock.EnterScope())
            {
                _waitSemaphores.Add(semaphore);
                _waitStages.Add(stage);
            }
        }

        public void AddSignalSemaphore(VkSemaphore semaphore)
        {
            lock (_semaphoreLock)
            {
                _signalSemaphores.Add(semaphore);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<CommandBuffer> GetSubmissionList()
        {
            if (_reusableSubmissionList == null)
            {
                _reusableSubmissionList = new List<CommandBuffer>(64);
            }
            else
            {
                _reusableSubmissionList.Clear();
            }
            return _reusableSubmissionList;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReturnSubmissionList(List<CommandBuffer> list)
        {
            // Optional: trim if list grew too large
            if (list.Capacity > 128 && list.Count < 128)
            {
                list.Capacity = 128;
            }
        }

        public void Dispose()
        {
            foreach (var (pool, batchPool) in _perThreadResources.Values)
            {
                while (batchPool.Count > 0)
                {
                    _ = batchPool.Pop();
                }
                pool.Dispose();
            }

            _perThreadResources.Dispose();
            StagingManager.Dispose();
            _fence.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}