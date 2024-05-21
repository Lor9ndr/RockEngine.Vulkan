using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class VulkanFence : VkObject
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private readonly Fence _fence;

        public VulkanFence(Vk api, VulkanLogicalDevice device, Fence fence)
        {
            _api = api;
            _device = device;
            _fence = fence;
        }

        public Fence Fence => _fence;

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
                    _api.DestroyFence(_device.Device, _fence, null);
                }

                _disposed = true;
            }
        }
    }
}
