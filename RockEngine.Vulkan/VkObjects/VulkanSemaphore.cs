using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanSemaphore : VkObject
    {
        private readonly Semaphore _semaphore;
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        public Semaphore Semaphore => _semaphore;

        public VulkanSemaphore(Vk api, VulkanLogicalDevice device, Semaphore semaphore)
        {
            _semaphore = semaphore;
            _api = api;
            _device = device;
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
                    _api.DestroySemaphore(_device.Device, _semaphore, null);
                }

                _disposed = true;
            }
        }
    }
}
