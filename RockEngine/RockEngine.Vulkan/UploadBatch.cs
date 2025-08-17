using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    /// <summary>
    /// Represents a batch of GPU upload commands with associated resources
    /// </summary>
    public sealed class UploadBatch : IDisposable
    {
        private readonly StagingManager _stagingManager;
        private readonly SubmitContext _submitContext;
        private readonly VkCommandBuffer _commandBuffer;
        private readonly List<Action> _disposables;
        private bool _isInUse;

        public List<VkSemaphore> SignalSemaphores { get; } = new List<VkSemaphore>(2);
        public Dictionary<VkSemaphore, PipelineStageFlags> WaitSemaphores { get; } = new Dictionary<VkSemaphore, PipelineStageFlags>(2);
        public VkCommandBuffer CommandBuffer => _commandBuffer;
        public IReadOnlyList<Action> Disposables => _disposables;

        public SubmitContext SubmitContext => _submitContext;


        public UploadBatch(StagingManager stagingManager, SubmitContext submitContext, VkCommandBuffer commandBuffer)
        {
            _stagingManager = stagingManager;
            _submitContext = submitContext;
            _commandBuffer = commandBuffer;
            _disposables = new List<Action>(4);
            BeginCommandBuffer();
        }
        /// <summary>
        /// Starts recording commands for this batch
        /// </summary>
        public void BeginCommandBuffer()
        {
            _commandBuffer.Begin(new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            });
        }

        public void AddWaitSemaphore(VkSemaphore semaphore, PipelineStageFlags stage)
            => WaitSemaphores[semaphore] = stage;

        public void AddSignalSemaphore(VkSemaphore semaphore)
            => SignalSemaphores.Add(semaphore);

        /// <summary>
        /// Prepares the batch for reuse by resetting state and command buffer
        /// </summary>
        public void ResetForPool()
        {
            if (!_isInUse) return;

            // Reset command buffer and clear state
            _commandBuffer.Reset(CommandBufferResetFlags.None);
            _disposables.Clear();
            SignalSemaphores.Clear();
            WaitSemaphores.Clear();
            _isInUse = false;

            BeginCommandBuffer();
        }

        /// <summary>
        /// Stages data to a GPU buffer
        /// </summary>
        public void StageToBuffer<T>(
            Span<T> data,
            VkBuffer destination,
            ulong dstOffset,
            ulong size) where T : unmanaged
        {
            if (size == 0)
                throw new InvalidOperationException("Size cannot be 0");

            if (!_stagingManager.TryStage(this, data, out var srcOffset, out _))
                throw new InvalidOperationException("Staging buffer overflow");

            _commandBuffer.CopyBuffer(
                _stagingManager.StagingBuffer,
                destination,
                new BufferCopy(srcOffset, dstOffset, size)
            );
        }

        /// <summary>
        /// Submits the batch for execution
        /// </summary>
        public void Submit()
        {
            End();
            _submitContext.AddSubmission(this);
        }
        public void End()
        {
            _commandBuffer.End();
        }

        /// <summary>
        /// Adds a resource dependency that must be disposed after execution
        /// </summary>
        public void AddDependency(IDisposable disposable)
            => _disposables.Add(disposable.Dispose);
        public void AddDependency(Action action)
           => _disposables.Add(action);

        /// <summary>
        /// Marks the batch as in-use before submission
        /// </summary>
        public void MarkInUse()
        {
            _isInUse = true;
        }

        /// <summary>
        /// Returns the batch to its pool for reuse
        /// </summary>
        public void Dispose()
        {
            _submitContext.ReturnBatchToPool(this);
        }

        public void PipelineBarrier(
     PipelineStageFlags srcStage,
     PipelineStageFlags dstStage,
     MemoryBarrier[]? memoryBarriers = null,
     BufferMemoryBarrier[]? bufferMemoryBarriers = null,
     ImageMemoryBarrier[]? imageMemoryBarriers = null)
        {
            _commandBuffer.PipelineBarrier(srcStage, dstStage,DependencyFlags.None, (uint)(memoryBarriers?.Length ?? 0), memoryBarriers, (uint)(bufferMemoryBarriers?.Length ?? 0),bufferMemoryBarriers, (uint)(imageMemoryBarriers?.Length ?? 0), imageMemoryBarriers);
        }
    }
}