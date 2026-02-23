using Silk.NET.Vulkan;

using ZLinq;

namespace RockEngine.Vulkan
{
    public class VkCommandBuffer : VkObject<CommandBuffer>
    {
        private readonly VulkanContext _context;
        private readonly VkCommandPool _commandPool;
        private readonly bool _isSecondary;
        private bool _isInRecordingState = false;

        public VkCommandPool CommandPool => _commandPool;

        public bool IsInRecordingState => _isInRecordingState;
        public bool IsSecondary => _isSecondary;


        private static uint _id = 0;
        private readonly uint _cmdID;

        public VkCommandBuffer(VulkanContext context, in CommandBuffer commandBuffer, VkCommandPool commandPool, bool isSecondary = false)
            : base(in commandBuffer)
        {
            _context = context;
            _commandPool = commandPool;
            _isSecondary = isSecondary;
            _cmdID = _id++;
            LabelObject($"Cmd ({_cmdID})");
        }

        public void Begin(in CommandBufferBeginInfo beginInfo)
        {
            VulkanContext.Vk.BeginCommandBuffer(_vkObject, in beginInfo)
                .VkAssertResult("Failed to begin command buffer!");
            _isInRecordingState = true;

        }
        public void Begin(CommandBufferUsageFlags usageFlags)
        {
            Begin(new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = usageFlags
            });
        }
        public void Begin(CommandBufferUsageFlags usageFlags, in CommandBufferInheritanceInfo inheritanceInfo)
        {
            unsafe
            {
                fixed(CommandBufferInheritanceInfo* pInheritanceInfo = &inheritanceInfo)
                {
                    Begin(new CommandBufferBeginInfo()
                    {
                        SType = StructureType.CommandBufferBeginInfo,
                        Flags = usageFlags,
                        PInheritanceInfo = pInheritanceInfo
                    });
                }
            }
        }
        public void Begin(in CommandBufferBeginInfo beginInfo, Action untilEndAction)
        {
            if (!_isInRecordingState)
            {
                Begin(in beginInfo);
            }
            untilEndAction();
            End();
        }

        public void End()
        {
            VulkanContext.Vk.EndCommandBuffer(_vkObject)
            .VkAssertResult("Failed to end command buffer!");
            _isInRecordingState = false;
        }

        public void CopyBuffer(VkBuffer srcBuffer, VkBuffer dstBuffer, ulong size)
        {
            BufferCopy bufferCopy = new BufferCopy()
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = size
            };
            VulkanContext.Vk.CmdCopyBuffer(_vkObject, srcBuffer, dstBuffer, 1, in bufferCopy);
        }
        public void CopyBuffer(VkBuffer srcBuffer, VkBuffer dstBuffer, in BufferCopy bufferCopy)
        {
            VulkanContext.Vk.CmdCopyBuffer(_vkObject, srcBuffer, dstBuffer, 1, in bufferCopy);
        }

        public void SetViewport(in Viewport viewport)
        {
            VulkanContext.Vk.CmdSetViewport(_vkObject, 0, 1, in viewport);
        }

        public void SetScissor(in Rect2D scissor)
        {
            VulkanContext.Vk.CmdSetScissor(_vkObject, 0, 1, in scissor);
        }

        public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            VulkanContext.Vk.CmdDraw(_vkObject, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            VulkanContext.Vk.CmdDrawIndexed(_vkObject, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        public void BeginRenderPass(in RenderPassBeginInfo renderPassBeginInfo, SubpassContents contents)
        {
            VulkanContext.Vk.CmdBeginRenderPass(this, in renderPassBeginInfo, contents);
        }
        public void EndRenderPass()
        {
            VulkanContext.Vk.CmdEndRenderPass(this);
        }
        public void BindVertexBuffer(VkBuffer vertexBuffer, in ulong offset = 0)
        {
            var buffer = vertexBuffer.VkObjectNative;
            VulkanContext.Vk.CmdBindVertexBuffers(_vkObject, 0, 1, ref buffer, in offset);
        }

        public void BindIndexBuffer(VkBuffer indexBuffer, ulong offset = 0, IndexType indexType = IndexType.Uint32)
        {
            VulkanContext.Vk.CmdBindIndexBuffer(_vkObject, indexBuffer, offset, indexType);
        }
        public void BindPipeline(VkPipeline pipeline, PipelineBindPoint pipelineBindPoint = PipelineBindPoint.Graphics)
        {
            VulkanContext.Vk.CmdBindPipeline(this, pipelineBindPoint, pipeline);
        }

        public static VkCommandBuffer Create(in CommandBufferAllocateInfo ai, VkCommandPool commandPool)
        {
            return commandPool.AllocateCommandBuffer(in ai);
        }
        public void NextSubpass(SubpassContents subPassContents)
        {
            VulkanContext.Vk.CmdNextSubpass(this, subPassContents);
        }
        public void Reset(CommandBufferResetFlags flags)
        {
            VulkanContext.Vk.ResetCommandBuffer(this, flags)
                        .VkAssertResult("Failed to reset commandBuffer");
        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.CommandBuffer, name);
        public DebugLabelScope NameAction(string name, float[] color)
        {
            return _context.DebugUtils.CmdDebugLabelScope(this, name, color);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Dispose managed state (managed objects).
            }
            
            _disposed = true;
            _commandPool.FreeCommandBuffer(this);
            _vkObject = default;
        }

        public void BindDescriptorSet(PipelineBindPoint pipelineBindPoint, VkPipelineLayout deferredPipelineLayout, uint firstSet, ReadOnlySpan<DescriptorSet> descriptorSets, ReadOnlySpan<uint> dynamicOffsets = default)
        {
            //lock (_commandPool._lock)
            {
                VulkanContext.Vk.CmdBindDescriptorSets(this, pipelineBindPoint, deferredPipelineLayout, firstSet, descriptorSets, dynamicOffsets);
            }
        }

        public unsafe void ExecuteSecondary(VkCommandBuffer[] secondaryCommandBuffer)
        {
            //lock (_commandPool._lock)
            {
                fixed (CommandBuffer* ptr = secondaryCommandBuffer.AsValueEnumerable().Select(s=>s.VkObjectNative).ToArray())
                {
                    VulkanContext.Vk.CmdExecuteCommands(
                    commandBuffer: _vkObject,
                    commandBufferCount: (uint)secondaryCommandBuffer.Length,
                    pCommandBuffers: ptr
                );
                }
            }
        }

        public unsafe void ExecuteSecondary(VkCommandBuffer secondaryCommandBuffer)
        {
            var cmd = secondaryCommandBuffer.VkObjectNative;
            VulkanContext.Vk.CmdExecuteCommands(commandBuffer: _vkObject, commandBufferCount: 1, pCommandBuffers: in cmd);
        }
        public unsafe void ExecuteSecondary(in CommandBuffer secondaryCommandBuffer)
        {
            VulkanContext.Vk.CmdExecuteCommands(commandBuffer: _vkObject, commandBufferCount: 1, pCommandBuffers: in secondaryCommandBuffer);
        }

        public void DrawIndirect(VkBuffer buffer, uint drawCount, ulong offset, uint stride)
        {
            //lock (_commandPool._lock)
            {
                VulkanContext.Vk.CmdDrawIndexedIndirect(this, buffer, offset, drawCount, stride);
            }
        }

        public void SetViewportAndScissor(Extent2D extent)
        {
            var rect2d = new Rect2D(new Offset2D(), extent);
            var viewPort = new Viewport(0, 0, extent.Width, extent.Height, 0, 1);
            VulkanContext.Vk.CmdSetScissor(this, 0, 1, in rect2d);
            VulkanContext.Vk.CmdSetViewport(this, 0, 1, in viewPort);
        }

        public void PushConstants<T>(PipelineLayout layout, ShaderStageFlags stageFlags, uint offset, uint size, ref T value) where T : unmanaged
        {
            VulkanContext.Vk.CmdPushConstants(this, layout, stageFlags, offset, size, ref value);
        }
        public void PushConstants<T>(PipelineLayout layout, ShaderStageFlags stageFlags, uint offset, uint size,  Span<T> value) where T : unmanaged
        {
            VulkanContext.Vk.CmdPushConstants(this, layout, stageFlags, offset, size, value);
        }

        public unsafe void PushConstants(PipelineLayout layout, ShaderStageFlags stageFlags, uint offset, uint size, void* value)
        {
            //lock (_commandPool._lock)
            {
                VulkanContext.Vk.CmdPushConstants(this, layout, stageFlags, offset, size, value);
            }
        }

        public void WriteTimestamp(PipelineStageFlags stage, VkQueryPool pool, uint query)
        {
            VulkanContext.Vk.CmdWriteTimestamp(this, stage, pool, query);
        }

        public unsafe void PipelineBarrier(PipelineStageFlags srcStageMask, PipelineStageFlags dstStageMask, DependencyFlags dependencyFlags, uint memoryBarrierCount, MemoryBarrier* pMemoryBarriers, uint bufferMemoryBarrierCount, BufferMemoryBarrier* pBufferMemoryBarriers, uint imageMemoryBarrierCount, in ImageMemoryBarrier pImageMemoryBarriers)
        {
            VulkanContext.Vk.CmdPipelineBarrier(this, srcStageMask, dstStageMask, dependencyFlags, memoryBarrierCount, pMemoryBarriers, bufferMemoryBarrierCount, pBufferMemoryBarriers, imageMemoryBarrierCount, in pImageMemoryBarriers);
        }
        public void PipelineBarrier(PipelineStageFlags srcStageMask, PipelineStageFlags dstStageMask, DependencyFlags dependencyFlags, uint memoryBarrierCount, Span<MemoryBarrier> pMemoryBarriers, uint bufferMemoryBarrierCount, Span<BufferMemoryBarrier> pBufferMemoryBarriers, uint imageMemoryBarrierCount, Span<ImageMemoryBarrier> pImageMemoryBarriers)
        {
            VulkanContext.Vk.CmdPipelineBarrier(this, srcStageMask, dstStageMask, dependencyFlags, memoryBarrierCount, pMemoryBarriers, bufferMemoryBarrierCount, pBufferMemoryBarriers, imageMemoryBarrierCount, pImageMemoryBarriers);
        }

        public void PipelineBarrier2(in DependencyInfo dependencyInfo)
        {
            VulkanContext.Vk.CmdPipelineBarrier2(this, in dependencyInfo);
        }

        public unsafe void CopyBufferToImage(VkBuffer srcBuffer, VkImage dstImage, ImageLayout dstImageLayout, uint regionCount, BufferImageCopy* pRegions)
        {
            VulkanContext.Vk.CmdCopyBufferToImage(this, srcBuffer, dstImage, dstImageLayout, regionCount, pRegions);
        }
        public unsafe void CopyBufferToImage(VkBuffer srcBuffer, VkImage dstImage, ImageLayout dstImageLayout, uint regionCount, Span<BufferImageCopy> pRegions)
        {
            VulkanContext.Vk.CmdCopyBufferToImage(this, srcBuffer, dstImage, dstImageLayout, regionCount, pRegions);
        }
        public  void CopyImageToBuffer(VkImage srcImage, ImageLayout srcImageLayout, VkBuffer dstBuffer, in BufferImageCopy pRegions)
        {
            VulkanContext.Vk.CmdCopyImageToBuffer(this, srcImage , srcImageLayout, dstBuffer, 1, in pRegions);
        }
        public void CopyImageToBuffer(VkImage srcImage, ImageLayout srcImageLayout, VkBuffer dstBuffer,  Span<BufferImageCopy> pRegions)
        {
            VulkanContext.Vk.CmdCopyImageToBuffer(this, srcImage, srcImageLayout, dstBuffer, (uint)pRegions.Length, pRegions);
        }
        public unsafe void CopyBufferToImage(VkBuffer srcBuffer, VkImage dstImage, ImageLayout dstImageLayout, uint regionCount, in BufferImageCopy pRegions)
        {
            Vk.CmdCopyBufferToImage(this, srcBuffer, dstImage, dstImageLayout, regionCount, in pRegions);
        }

        public void ResetQueryPool(VkQueryPool queryPool, uint firstQuery, uint queryCount)
        {
            Vk.CmdResetQueryPool(this, queryPool, firstQuery, queryCount);
        }

        public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            Vk.CmdDispatch(this, groupCountX, groupCountY, groupCountZ);
        }

        public void ClearDepthStencilImage(VkImage image, ImageLayout layout, float depth, uint stencil,in ImageSubresourceRange imageSubresourceRange)
        {
            ClearDepthStencilValue clearDepthStencilValue = new ClearDepthStencilValue()
            {
                Depth = depth,
                Stencil = stencil
            };
            Vk.CmdClearDepthStencilImage(this, image, layout,in clearDepthStencilValue, 1, in imageSubresourceRange);
        }

        public void BeginQuery(VkQueryPool vkQueryPool, uint query, QueryControlFlags flags)
        {
            Vk.CmdBeginQuery(this, vkQueryPool, query, flags);
        }

        internal void EndQuery(VkQueryPool vkQueryPool, uint query)
        {
            Vk.CmdEndQuery(this, vkQueryPool, query);
        }

        internal void BlitImage(VkImage srcImage, ImageLayout srcImageLayout, VkImage dstImage, ImageLayout dstImageLayout, in ImageBlit pRegions, Filter filter)
        {
            Vk.CmdBlitImage(this, srcImage, srcImageLayout, dstImage, dstImageLayout, 1, in pRegions, filter);
        }
    }
}