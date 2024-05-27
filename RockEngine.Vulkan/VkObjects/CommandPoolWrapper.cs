using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class CommandPoolWrapper:VkObject
    {
        private readonly Vk _api;
        private readonly LogicalDeviceWrapper _device;
        private readonly CommandPool _commandPool;
        public CommandPool CommandPool => _commandPool;

        public CommandPoolWrapper(Vk api, LogicalDeviceWrapper device, CommandPool commandPool)
        {
            _api = api;
            _device = device;
            _commandPool = commandPool;
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
                    _api.DestroyCommandPool(_device.Device, _commandPool, null);
                }

                _disposed = true;
            }
        }
    }
}
