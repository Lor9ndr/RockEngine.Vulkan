using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanFramebuffer : VkObject
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private readonly Framebuffer _framebuffer;
        public Framebuffer Framebuffer => _framebuffer;

        public VulkanFramebuffer(Vk api, VulkanLogicalDevice device, Framebuffer framebuffer)
        {
            _api = api;
            _device = device;
            _framebuffer = framebuffer;
        }


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
                    _api.DestroyFramebuffer(_device.Device, _framebuffer, null);
                }

                _disposed = true;
            }
        }
    }
}
