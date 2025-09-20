using Silk.NET.Vulkan;

using System.Drawing;
using System.Runtime.CompilerServices;

using static RockEngine.Vulkan.SubmitContext;

namespace RockEngine.Vulkan
{
    public sealed class UploadBatch : IDisposable
    {
        private readonly StagingManager _stagingManager;
        private readonly SubmitContext _submitContext;
        private readonly VkCommandBuffer _commandBuffer;
        private readonly List<Action> _disposables;
        private bool _isInUse;
        private readonly CommandBufferLevel _level;
        private CommandBufferInheritanceInfo? _inheritanceInfo;

        private readonly List<UploadBatch> _secondaryBatches = new List<UploadBatch>();

        public List<VkSemaphore> SignalSemaphores { get; } = new List<VkSemaphore>(2);
        public Dictionary<VkSemaphore, PipelineStageFlags> WaitSemaphores { get; } = new Dictionary<VkSemaphore, PipelineStageFlags>(2);
        public VkCommandBuffer CommandBuffer => _commandBuffer;
        public IReadOnlyList<Action> Disposables => _disposables;
        public SubmitContext SubmitContext => _submitContext;
        internal CommandPoolContext Context { get; }
        public CommandBufferLevel Level => _level;

        public CommandBufferInheritanceInfo? InheritanceInfo
        {
            get => _inheritanceInfo; 
            set => _inheritanceInfo = value; 
        }

        internal UploadBatch(
            CommandPoolContext context,
            StagingManager stagingManager,
            SubmitContext submitContext,
            VkCommandBuffer commandBuffer,
            CommandBufferLevel level,
            CommandBufferInheritanceInfo? inheritanceInfo = null)
        {
            Context = context;
            _stagingManager = stagingManager;
            _submitContext = submitContext;
            _commandBuffer = commandBuffer;
            _level = level;
            _inheritanceInfo = inheritanceInfo;
            _disposables = new List<Action>(4);
        }

        public void BeginCommandBuffer()
        {
            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
            };

            if (_level == CommandBufferLevel.Secondary)
            {
                if (!_inheritanceInfo.HasValue)
                    throw new InvalidOperationException("Secondary command buffers require inheritance info.");

                // For secondary buffers, we always use SimultaneousUseBit for versioned batches
                // and OneTimeSubmitBit for one-time batches
                beginInfo.Flags =   CommandBufferUsageFlags.OneTimeSubmitBit | CommandBufferUsageFlags.RenderPassContinueBit;

                unsafe
                {
                    var value = _inheritanceInfo.Value;
                    beginInfo.PInheritanceInfo = (CommandBufferInheritanceInfo*)Unsafe.AsPointer(ref value);
                }
            }
            else
            {
                // For primary buffers
                beginInfo.Flags =  CommandBufferUsageFlags.OneTimeSubmitBit ;
            }

            _commandBuffer.Begin(beginInfo);
        }

        public void AddWaitSemaphore(VkSemaphore semaphore, PipelineStageFlags stage)
            => WaitSemaphores[semaphore] = stage;

        public void AddSignalSemaphore(VkSemaphore semaphore)
            => SignalSemaphores.Add(semaphore);

        public void ResetLists()
        {
            if (!_isInUse) return;

            _disposables.Clear();
            SignalSemaphores.Clear();
            WaitSemaphores.Clear();
            _secondaryBatches.Clear();
            _isInUse = false;
        }


        public void ResetCommandBuffer()
        {
            _commandBuffer.Reset(CommandBufferResetFlags.ReleaseResourcesBit);
            BeginCommandBuffer();
        }


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

        public void Submit()
        {
            End();
            _submitContext.AddSubmission(this);
        }

        
        public void ExecuteCommands(UploadBatch secondaryBatch)
        {
            if (_level != CommandBufferLevel.Primary)
                throw new InvalidOperationException("Only primary command buffers can execute secondary command buffers.");

            if (secondaryBatch.Level != CommandBufferLevel.Secondary)
                throw new InvalidOperationException("Only secondary command buffers can be executed.");

            // Add the secondary batch as a dependency to ensure it's disposed properly
            _secondaryBatches.Add(secondaryBatch);
            AddDependency(secondaryBatch);

            _commandBuffer.ExecuteSecondary(secondaryBatch.CommandBuffer);
        }

        public void End()
        {
            _commandBuffer.End();
        }

        public void AddDependency(IDisposable disposable)
            => _disposables.Add(disposable.Dispose);

        public void AddDependency(Action action)
           => _disposables.Add(action);

        public void MarkInUse()
        {
            _isInUse = true;
        }

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
            _commandBuffer.PipelineBarrier(
                srcStage,
                dstStage,
                DependencyFlags.None,
                (uint)(memoryBarriers?.Length ?? 0),
                memoryBarriers,
                (uint)(bufferMemoryBarriers?.Length ?? 0),
                bufferMemoryBarriers,
                (uint)(imageMemoryBarriers?.Length ?? 0),
                imageMemoryBarriers
            );
        }

        public void CopyBuffer(VkBuffer srcBuffer, VkBuffer dstBuffer, ulong srcOffset, ulong dstOffset, ulong size)
        {
            var copyRegion = new BufferCopy
            {
                SrcOffset = srcOffset,
                DstOffset = dstOffset,
                Size = size
            };

            VulkanContext.Vk.CmdCopyBuffer(
                _commandBuffer,
                srcBuffer,
                dstBuffer,
                1,
                in copyRegion
            );
        }
    }
}