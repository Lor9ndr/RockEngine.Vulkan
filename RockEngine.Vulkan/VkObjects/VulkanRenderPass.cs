using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanRenderPass : VkObject
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private readonly RenderPass _renderPass;

        public VulkanRenderPass(Vk api, VulkanLogicalDevice device, RenderPass renderPass)
        {
            _api = api;
            _device = device;
            _renderPass = renderPass;

        }

        public RenderPass RenderPass => _renderPass;

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
                    _api.DestroyRenderPass(_device.Device, _renderPass, null);
                }

                _disposed = true;
            }
        }
    }
}
