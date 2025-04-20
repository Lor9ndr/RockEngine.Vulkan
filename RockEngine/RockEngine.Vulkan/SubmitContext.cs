using RockEngine.Core.Rendering.Managers;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan
{
    public sealed class SubmitContext : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly StagingManager _stagingManager;
        private readonly ThreadLocal<(VkCommandPool Pool, Stack<UploadBatch> BatchPool)> _perThreadResources;
        private readonly ConcurrentQueue<CommandBuffer> _pendingSubmissions = new();
        private readonly VkFence _fence;
        private readonly ConcurrentBag<UploadBatch> _activeBatches = new();

        [ThreadStatic]
        private static List<CommandBuffer> _reusableSubmissionList;

        public SubmitContext(VulkanContext context)
        {
            _context = context;
            _stagingManager = new StagingManager(context);
            _fence = VkFence.CreateNotSignaled(context);

            _perThreadResources = new ThreadLocal<(VkCommandPool, Stack<UploadBatch>)>(
            () => {
                var pool = VkCommandPool.Create(
                    context,
                    CommandPoolCreateFlags.TransientBit |
                    CommandPoolCreateFlags.ResetCommandBufferBit,
                    context.Device.QueueFamilyIndices.GraphicsFamily.Value
                );
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

            var newBatch = new UploadBatch(_context, _stagingManager, pool, this);
            _activeBatches.Add(newBatch); // Track new batch
            return newBatch;
        }

        public void AddSubmission(CommandBuffer commandBuffer)
        {
            _pendingSubmissions.Enqueue(commandBuffer);
        }

        public async Task FlushAsync()
        {
            if (_pendingSubmissions.IsEmpty) return;

            // Reuse thread-static list to avoid allocations
            var submissions = GetSubmissionList();

            while (_pendingSubmissions.TryDequeue(out var cmd))
            {
                submissions.Add(cmd);
            }

            _fence.Reset();

            // Use span to avoid array allocation
            var commands = CollectionsMarshal.AsSpan(submissions);
            unsafe
            {
                fixed (CommandBuffer* pCommands = commands)
                {
                    var submitInfo = new SubmitInfo
                    {
                        SType = StructureType.SubmitInfo,
                        CommandBufferCount = (uint)commands.Length,
                        PCommandBuffers = pCommands
                    };

                    _context.Device.GraphicsQueue.Submit(submitInfo, _fence);
                }
            }

            await _fence.WaitAsync();

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
            _activeBatches.Clear(); // Clear for next frame

            _stagingManager.Reset();
            ReturnSubmissionList(submissions);
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
            if (list.Capacity > 128) list.Capacity = 128;
        }

        public void Dispose()
        {
            foreach (var (pool, batchPool) in _perThreadResources.Values)
            {
                while (batchPool.Count > 0)
                {
                    batchPool.Pop().Dispose();
                }
                pool.Dispose();
            }

            _perThreadResources.Dispose();
            _stagingManager.Dispose();
            _fence.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}