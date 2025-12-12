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
        private readonly List<IDisposable> _disposables;
        private bool _isInUse;
        private readonly CommandBufferLevel _level;
        private CommandBufferInheritanceInfo? _inheritanceInfo;

        private readonly List<UploadBatch> _secondaryBatches = new List<UploadBatch>();

        public List<VkSemaphore> SignalSemaphores { get; } = new List<VkSemaphore>(2);
        public Dictionary<VkSemaphore, PipelineStageFlags> WaitSemaphores { get; } = new Dictionary<VkSemaphore, PipelineStageFlags>(2);
        internal VkCommandBuffer CommandBuffer => _commandBuffer;
        public IReadOnlyList<IDisposable> Disposables => _disposables;
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
            _disposables = new List<IDisposable>(4);
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
                {
                    throw new InvalidOperationException("Secondary command buffers require inheritance info.");
                }

                // For secondary buffers, we always use SimultaneousUseBit for versioned batches
                // and OneTimeSubmitBit for one-time batches
                beginInfo.Flags = CommandBufferUsageFlags.OneTimeSubmitBit | CommandBufferUsageFlags.RenderPassContinueBit;

                unsafe
                {
                    var value = _inheritanceInfo.Value;
                    beginInfo.PInheritanceInfo = (CommandBufferInheritanceInfo*)Unsafe.AsPointer(ref value);
                }
            }
            else
            {
                // For primary buffers
                beginInfo.Flags = CommandBufferUsageFlags.OneTimeSubmitBit;
            }

            _commandBuffer.Begin(beginInfo);
        }

        public void AddWaitSemaphore(VkSemaphore semaphore, PipelineStageFlags stage)
            => WaitSemaphores[semaphore] = stage;

        public void AddSignalSemaphore(VkSemaphore semaphore)
            => SignalSemaphores.Add(semaphore);

        public void ResetLists()
        {
            if (!_isInUse)
            {
                return;
            }

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
            ReadOnlySpan<T> data,
            VkBuffer destination,
            ulong dstOffset,
            ulong size) where T : unmanaged
        {
            if (size == 0)
            {
                throw new InvalidOperationException("Size cannot be 0");
            }

            if (!_stagingManager.TryStage(this, data, out var srcOffset, out _))
            {
                throw new InvalidOperationException("Staging buffer overflow");
            }

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
            {
                throw new InvalidOperationException("Only primary command buffers can execute secondary command buffers.");
            }

            if (secondaryBatch.Level != CommandBufferLevel.Secondary)
            {
                throw new InvalidOperationException("Only secondary command buffers can be executed.");
            }

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
            => _disposables.Add(disposable);


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

        public void PipelineBarrier(
           PipelineStageFlags srcStage,
           PipelineStageFlags dstStage,
           Span<MemoryBarrier> memoryBarriers)
        {
            _commandBuffer.PipelineBarrier(
                srcStage,
                dstStage,
                DependencyFlags.None,
                (uint)(memoryBarriers.Length),
                memoryBarriers,
                0,
                [],
                0,
                []
            );
        }

        public void PipelineBarrier(
           PipelineStageFlags srcStage,
           PipelineStageFlags dstStage,
           Span<ImageMemoryBarrier> imageMemoryBarriers)
        {
            _commandBuffer.PipelineBarrier(
                srcStage,
                dstStage,
                DependencyFlags.None,
                0,
                [],
                0,
                [],
                (uint)imageMemoryBarriers.Length,
                imageMemoryBarriers
            );
        }
        public void PipelineBarrier(
          PipelineStageFlags srcStage,
          PipelineStageFlags dstStage,
          Span<BufferMemoryBarrier> bufferMemoryBarriers)
        {
            _commandBuffer.PipelineBarrier(
                srcStage,
                dstStage,
                DependencyFlags.None,
                0,
                default,
                (uint)(bufferMemoryBarriers.Length),
                bufferMemoryBarriers,
                 0,
                default
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
        public void CopyBuffer(VkBuffer srcBuffer, VkBuffer dstBuffer, in BufferCopy copyRegion)
        {
            VulkanContext.Vk.CmdCopyBuffer(
                _commandBuffer,
                srcBuffer,
                dstBuffer,
                1,
                in copyRegion
            );
        }

        public void CopyImage(
            VkImage source,
            ImageLayout srcLayout,
            VkImage destination,
            ImageLayout dstLayout,
            uint srcLayer,
            uint dstLayer,
            uint layerCount)
        {
            // Validate layer ranges
            if (srcLayer + layerCount > source.ArrayLayers)
            {
                throw new ArgumentException(
                    $"Source layer range [{srcLayer}-{srcLayer + layerCount - 1}] " +
                    $"exceeds source array layers ({source.ArrayLayers})");
            }

            if (dstLayer + layerCount > destination.ArrayLayers)
            {
                throw new ArgumentException(
                    $"Destination layer range [{dstLayer}-{dstLayer + layerCount - 1}] " +
                    $"exceeds destination array layers ({destination.ArrayLayers})");
            }

            // Validate image dimensions are compatible
            if (source.Extent.Width != destination.Extent.Width ||
                source.Extent.Height != destination.Extent.Height)
            {
                throw new ArgumentException(
                    $"Image copy between different dimensions: " +
                    $"{source.Extent.Width}x{source.Extent.Height} -> " +
                    $"{destination.Extent.Width}x{destination.Extent.Height}");
            }

            // For depth/stencil images, we should use DepthBit aspect
            var srcAspect = source.AspectFlags;
            var dstAspect = destination.AspectFlags;

            // Create image copy regions for each layer
            var regions = new ImageCopy[1]
            {
                new ImageCopy()
                {
                    SrcSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = srcAspect,
                        MipLevel = 0,
                        BaseArrayLayer = srcLayer,
                        LayerCount = layerCount, // Use layerCount instead of source.ArrayLayers
                    },
                    SrcOffset = new Offset3D(0, 0, 0),
                    DstSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = dstAspect,
                        MipLevel = 0,
                        BaseArrayLayer = dstLayer,
                        LayerCount = layerCount // Use layerCount instead of destination.ArrayLayers
                    },
                    DstOffset = new Offset3D(0, 0, 0),
                    Extent = new Extent3D
                    {
                        Width = source.Extent.Width,
                        Height = source.Extent.Height,
                        Depth = 1
                    }
                }
            };

            // Perform the image copy
            VulkanContext.Vk.CmdCopyImage(
                _commandBuffer,
                source,
                srcLayout,
                destination,
                dstLayout,
                (uint)regions.Length,
                regions
            );
        }

        public void WriteTimestamp(PipelineStageFlags pipelineStage, VkQueryPool queryPool, uint query) => VulkanContext.Vk.CmdWriteTimestamp(_commandBuffer, pipelineStage, queryPool, query);

        public void LabelObject(string label)
        {
            _commandBuffer.LabelObject(label);
        }

        public unsafe void PushConstants(VkPipelineLayout layout, ShaderStageFlags stageFlags, uint offset, uint size, byte* dataPtr)
        {
            _commandBuffer.PushConstants(layout,stageFlags,offset,size,dataPtr);
        }
        public unsafe void PushConstants<T>(VkPipelineLayout layout, ShaderStageFlags stageFlags, uint offset, uint size, Span<T> data) where T:unmanaged
        {
            _commandBuffer.PushConstants(layout, stageFlags, offset, size, data);
        }
        public unsafe void PushConstants<T>(VkPipelineLayout layout, ShaderStageFlags stageFlags, uint offset, uint size, ref T data) where T : unmanaged
        {
            _commandBuffer.PushConstants(layout, stageFlags, offset, size, ref data);
        }

        public void BindDescriptorSets(PipelineBindPoint pipelineBindPoint, VkPipelineLayout pipelineLayout, uint minSetIndex,  Span<DescriptorSet> descriptorSets, Span<uint> dynamicOffsets)
        {
            _commandBuffer.BindDescriptorSet(pipelineBindPoint, pipelineLayout, minSetIndex, descriptorSets, dynamicOffsets);

        }

        public void BindPipeline(VkPipeline pipeline, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics)
        {
            _commandBuffer.BindPipeline(pipeline, bindPoint);
        }

        public void Dispatch(uint groupsX, uint groupsY, uint groupsZ)
        {
            _commandBuffer.Dispatch(groupsX, groupsY, groupsZ);

        }

        public DebugLabelScope NameAction(string name, float[] value)
        {
            return _commandBuffer.NameAction(name, value);
        }

        public void BeginRenderPass(in RenderPassBeginInfo renderPassBeginInfo, SubpassContents subpassContents)
        {
            _commandBuffer.BeginRenderPass(renderPassBeginInfo, subpassContents);
        }

        public void NextSubpass(SubpassContents subpassContents)
        {
            _commandBuffer.NextSubpass(subpassContents);
        }

        public void EndRenderPass()
        {
            _commandBuffer.EndRenderPass();
        }

        public void CopyImageToBuffer(VkImage srcImage, ImageLayout srcImageLayout, VkBuffer dstBuffer, in BufferImageCopy pRegions)
        {
            _commandBuffer.CopyImageToBuffer(srcImage, srcImageLayout, dstBuffer,  pRegions);
        }

        public  void CopyBufferToImage(VkBuffer srcBuffer, VkImage dstImage, ImageLayout dstImageLayout,  Span<BufferImageCopy> pRegions)
        {
            _commandBuffer.CopyBufferToImage(srcBuffer,dstImage, dstImageLayout, (uint)pRegions.Length, pRegions);
        }

     
        public void CopyBufferToImage(VkBuffer srcBuffer, VkImage dstImage, ImageLayout dstImageLayout, in BufferImageCopy pRegions)
        {
            _commandBuffer.CopyBufferToImage(srcBuffer, dstImage, dstImageLayout, 1, pRegions);
        }

        internal void BindVertexBuffer(VkBuffer vertexBuffer, in ulong offset = 0)
        {
            _commandBuffer.BindVertexBuffer(vertexBuffer, in offset);
        }

        internal void BindIndexBuffer(VkBuffer vkBuffer, ulong indexOffset, IndexType type)
        {
            _commandBuffer.BindIndexBuffer(vkBuffer, indexOffset, type);
        }

        public void SetViewport(in Viewport viewport)
        {
            _commandBuffer.SetViewport(in viewport);
        }

        public void SetScissor(in Rect2D scissor)
        {
            _commandBuffer.SetScissor(in scissor);
        }

        public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            _commandBuffer.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        public void DrawIndexedIndirect(VkBuffer buffer, uint drawCount, ulong offset, uint stride)
        {
            _commandBuffer.DrawIndirect(buffer,drawCount,offset,stride);
        }

        public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            _commandBuffer.Draw(vertexCount,instanceCount,firstVertex,firstInstance);
        }
    }
}