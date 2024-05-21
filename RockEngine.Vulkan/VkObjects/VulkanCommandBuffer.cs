using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanCommandBuffer : VkObject
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private readonly CommandBuffer _commandBuffer;

        public VulkanCommandBuffer(Vk api, VulkanLogicalDevice device, CommandBuffer commandBuffer)
        {
            _api = api;
            _device = device;
            _commandBuffer = commandBuffer;
        }

        public CommandBuffer CommandBuffer => _commandBuffer;
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                unsafe
                {
                }

                _disposed = true;
            }
        }
    }
}
