using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanFenceBuilder
    {
        private FenceCreateFlags _flags;
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;

        public VulkanFenceBuilder(Vk api, VulkanLogicalDevice device)
        {
            _api = api;
            _device = device;
        }
        public VulkanFenceBuilder WithFlags(FenceCreateFlags flags)
        {
            _flags = flags;
            return this;
        }

        public VulkanFence Build()
        {
            FenceCreateInfo ci = new FenceCreateInfo()
            {
                SType = StructureType.FenceCreateInfo,
                Flags = _flags
            };
            unsafe
            {
                _api.CreateFence(_device.Device, in ci, null, out Fence fence);
                return new VulkanFence(_api, _device, fence);
            }
        }
    }
}
