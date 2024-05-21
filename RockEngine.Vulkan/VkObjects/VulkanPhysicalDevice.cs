using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public partial class VulkanPhysicalDevice : VkObject
    {
        private PhysicalDevice _physicalDevice;
        private readonly Vk _api;

        public PhysicalDevice VulkanObject => _physicalDevice;

        public VulkanPhysicalDevice(PhysicalDevice physicalDevice, Vk api)
        {
            _physicalDevice = physicalDevice;
            _api = api;
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }
               
                // No need to destroy physicalDevice

                _disposed = true;
            }
        }
    }
}