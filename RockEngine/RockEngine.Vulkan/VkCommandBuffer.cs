using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkCommandBuffer : VkObject<CommandBuffer>, IBegginable<CommandBufferBeginInfo>
    {
        private readonly VulkanContext _context;
        private readonly VkCommandPool _commandPool;
        private readonly bool _isSecondary;
        private volatile bool _isInRecordingState = false;

        public VkCommandPool CommandPool => _commandPool;

        public bool IsInRecordingState => _isInRecordingState;
        public bool IsSecondary => _isSecondary;

        public VkCommandBuffer(VulkanContext context, in CommandBuffer commandBuffer, VkCommandPool commandPool, bool isSecondary = false)
            : base(in commandBuffer)
        {
            _context = context;
            _commandPool = commandPool;
            _isSecondary = isSecondary;
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
        public void Begin(in CommandBufferBeginInfo beginInfo, Action untilEndAction)
        {
            if (_isInRecordingState)
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
        public void BindVertexBuffer(VkBuffer vertexBuffer, ulong offset = 0)
        {
            var buffer = vertexBuffer.VkObjectNative;
            VulkanContext.Vk.CmdBindVertexBuffers(_vkObject, 0, 1, ref buffer, ref offset);
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
            VulkanContext.Vk.CmdBindDescriptorSets(this, pipelineBindPoint, deferredPipelineLayout, firstSet, descriptorSets, dynamicOffsets);
        }

        public unsafe void ExecuteSecondary(CommandBuffer[] secondaryCommandBuffer)
        {
            fixed (CommandBuffer* ptr = secondaryCommandBuffer)
            {
                VulkanContext.Vk.CmdExecuteCommands(
                commandBuffer: _vkObject,
                commandBufferCount: (uint)secondaryCommandBuffer.Length,
                pCommandBuffers: ptr
            );
            }

        }
        public unsafe void ExecuteSecondary(VkCommandBuffer secondaryCommandBuffer)
        {
            var cmd = secondaryCommandBuffer.VkObjectNative;
            VulkanContext.Vk.CmdExecuteCommands(commandBuffer: _vkObject, commandBufferCount: 1, pCommandBuffers: in cmd);
        }

        public void DrawIndirect(VkBuffer buffer, uint drawCount, uint offset, uint stride)
        {
            VulkanContext.Vk.CmdDrawIndexedIndirect(this, buffer, offset, drawCount, stride);
        }

        public void SetViewportAndScissor(Extent2D extent)
        {
            var rect2d = new Rect2D(new Offset2D(), extent);
            var viewPort = new Viewport(0, 0, extent.Width, extent.Height, 0, 1);
            VulkanContext.Vk.CmdSetScissor(this, 0, 1, in rect2d);
            VulkanContext.Vk.CmdSetViewport(this, 0, 1, in viewPort);
        }

        public void PushConstants<T>(PipelineLayout layout, ShaderStageFlags stageFlags, uint offset, uint size, ref T value) where T : unmanaged
            => VulkanContext.Vk.CmdPushConstants(this, layout, stageFlags, offset, size, ref value);

        public unsafe void PushConstants(PipelineLayout layout, ShaderStageFlags stageFlags, uint offset, uint size, void* value)
            => VulkanContext.Vk.CmdPushConstants(this, layout, stageFlags, offset, size, value);

        public void WriteTimestamp(PipelineStageFlags stage, VkQueryPool pool, uint query)
        {
            VulkanContext.Vk.CmdWriteTimestamp(this, stage, pool, query);
        }

        internal unsafe void PipelineBarrier(PipelineStageFlags srcStageMask, PipelineStageFlags dstStageMask, DependencyFlags dependencyFlags, uint memoryBarrierCount, MemoryBarrier* pMemoryBarriers, uint bufferMemoryBarrierCount, BufferMemoryBarrier* pBufferMemoryBarriers, uint imageMemoryBarrierCount, in ImageMemoryBarrier pImageMemoryBarriers)
        {
            VulkanContext.Vk.CmdPipelineBarrier(this, srcStageMask, dstStageMask, dependencyFlags, memoryBarrierCount, pMemoryBarriers, bufferMemoryBarrierCount, pBufferMemoryBarriers, imageMemoryBarrierCount, in pImageMemoryBarriers);

        }

        public unsafe void CopyBufferToImage(VkBuffer srcBuffer, VkImage dstImage, ImageLayout dstImageLayout, uint regionCount, BufferImageCopy* pRegions)
        {
            VulkanContext.Vk.CmdCopyBufferToImage(this, srcBuffer, dstImage, dstImageLayout, regionCount, pRegions);
        }
    }
}