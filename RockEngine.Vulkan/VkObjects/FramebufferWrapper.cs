using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class FramebufferWrapper : VkObject
    {
        private readonly Vk _api;
        private readonly LogicalDeviceWrapper _device;
        private readonly Framebuffer _framebuffer;
        public Framebuffer Framebuffer => _framebuffer;

        public FramebufferWrapper(Vk api, LogicalDeviceWrapper device, Framebuffer framebuffer)
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
