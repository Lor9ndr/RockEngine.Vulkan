using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan.VkObjects
{
    internal class SemaphoreWrapper : VkObject
    {
        private readonly Semaphore _semaphore;
        private readonly Vk _api;
        private readonly LogicalDeviceWrapper _device;
        public Semaphore Semaphore => _semaphore;

        public SemaphoreWrapper(Vk api, LogicalDeviceWrapper device, Semaphore semaphore)
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
