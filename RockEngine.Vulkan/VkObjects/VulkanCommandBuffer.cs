using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanCommandBuffer : VkObject, IBegginable<CommandBufferBeginInfo>
    {
        private readonly VulkanContext _context;
        public readonly  CommandBuffer CommandBuffer;
        private readonly VulkanCommandPool _commandPool;

        public VulkanCommandBuffer(VulkanContext context, CommandBuffer commandBuffer, VulkanCommandPool commandPool)
        {
            _context = context;
            CommandBuffer = commandBuffer;
            _commandPool = commandPool;
        }

        public void Begin(CommandBufferBeginInfo beginInfo)
        {
            _context.Api.BeginCommandBuffer(CommandBuffer, ref beginInfo);
        }

        public void End()
        {
            _context.Api.EndCommandBuffer(CommandBuffer);
        }
        
        public void CopyBuffer(VulkanBuffer srcBuffer, VulkanBuffer dstBuffer, ulong size)
        {
            BufferCopy bufferCopy = new BufferCopy()
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = size
            };
            _context.Api.CmdCopyBuffer(CommandBuffer, srcBuffer.Buffer, dstBuffer.Buffer,1, ref bufferCopy);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                _context.Api.FreeCommandBuffers(_context.Device.Device, _commandPool.CommandPool, 1, in CommandBuffer);

                _disposed = true;
            }
        }
    }
}
