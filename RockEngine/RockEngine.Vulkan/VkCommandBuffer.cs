using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkCommandBuffer : VkObject<CommandBuffer>, IBegginable<CommandBufferBeginInfo>
    {
        private readonly RenderingContext _context;
        private readonly VkCommandPool _commandPool;

        public VkCommandBuffer(RenderingContext context, in CommandBuffer commandBuffer, VkCommandPool commandPool)
            : base(in commandBuffer)
        {
            _context = context;
            _commandPool = commandPool;
        }

        public void Begin(in CommandBufferBeginInfo beginInfo)
        {
            RenderingContext.Vk.BeginCommandBuffer(_vkObject, in beginInfo)
                .VkAssertResult("Failed to begin command buffer!");
        }
        public void Begin(in CommandBufferBeginInfo beginInfo, Action untilEndAction)
        {
            Begin(in beginInfo);
            untilEndAction();
            End();
        }

        public void End()
        {
            RenderingContext.Vk.EndCommandBuffer(_vkObject)
                .VkAssertResult("Failed to end command buffer!");
        }

        public void CopyBuffer(VkBuffer srcBuffer, VkBuffer dstBuffer, ulong size)
        {
            BufferCopy bufferCopy = new BufferCopy()
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = size
            };
            RenderingContext.Vk.CmdCopyBuffer(_vkObject, srcBuffer, dstBuffer, 1, in bufferCopy);
        }

        public void SetViewport(in Viewport viewport)
        {
            RenderingContext.Vk.CmdSetViewport(_vkObject, 0, 1, in viewport);
        }

        public void SetScissor(in Rect2D scissor)
        {
            RenderingContext.Vk.CmdSetScissor(_vkObject, 0, 1, in scissor);
        }

        public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            RenderingContext.Vk.CmdDraw(_vkObject, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            RenderingContext.Vk.CmdDrawIndexed(_vkObject, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        public void BeginRenderPass(in RenderPassBeginInfo renderPassBeginInfo, SubpassContents contents)
        {
            RenderingContext.Vk.CmdBeginRenderPass(this,in renderPassBeginInfo, contents);
        }
        public void EndRenderPass()
        {
            RenderingContext.Vk.CmdEndRenderPass(this);
        }
        public void BindVertexBuffer(VkBuffer vertexBuffer, ulong offset = 0)
        {
            var buffer = vertexBuffer.VkObjectNative;
            RenderingContext.Vk.CmdBindVertexBuffers(_vkObject, 0, 1, ref buffer, ref offset);
        }

        public void BindIndexBuffer(VkBuffer indexBuffer, ulong offset = 0, IndexType indexType = IndexType.Uint32)
        {
            RenderingContext.Vk.CmdBindIndexBuffer(_vkObject, indexBuffer, offset, indexType);
        }
        public void BindPipeline(VkPipeline pipeline, PipelineBindPoint pipelineBindPoint = PipelineBindPoint.Graphics)
        {
            RenderingContext.Vk.CmdBindPipeline(this, pipelineBindPoint, pipeline);
        }

        public static VkCommandBuffer Create(in CommandBufferAllocateInfo ai, VkCommandPool commandPool)
        {
            return commandPool.AllocateCommandBuffer(in ai);
        }
        public void Reset(CommandBufferResetFlags flags)
        {
            RenderingContext.Vk.ResetCommandBuffer(this, flags)
                .VkAssertResult("Failed to reset commandBuffer");
        }


        /// <summary>
        /// Have no effect, 
        /// </summary>
        /// <param name="disposing"></param>
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
    }
}