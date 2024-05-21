using RockEngine.Vulkan.VkBuilders;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class VulkanLogicalDevice : VkObject
    {
        private Device _device;
        private readonly Vk _api;
        private readonly Queue _presentQueue;
        private readonly Queue _graphicsQueue;
        internal readonly QueueFamilyIndices QueueFamilyIndices;
        private readonly VulkanPhysicalDevice _physicalDevice;

        public Device Device => _device;

        public Queue PresentQueue => _presentQueue;

        public Queue GraphicsQueue => _graphicsQueue;

        public VulkanPhysicalDevice PhysicalDevice => _physicalDevice;

        internal VulkanLogicalDevice(Vk api, Device device, Queue graphicsQueue, Queue presentQueue, QueueFamilyIndices indices, VulkanPhysicalDevice physicalDevice)
        {
            _api = api;
            _device = device;
            _graphicsQueue = graphicsQueue;
            _presentQueue = presentQueue;
            QueueFamilyIndices = indices;
            _physicalDevice = physicalDevice;
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_device.Handle != IntPtr.Zero)
                {
                    _api.DestroyDevice(_device, null);
                    _device = default;
                }

                _disposed = true;
            }
        }
    }
}
