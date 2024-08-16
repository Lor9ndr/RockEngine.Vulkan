using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VulkanInitilizers
{
    public interface IVulkanContext
    {
        public Vk Api { get; }
        public CommandPoolManager CommandPoolManager { get;   }
        public DescriptorPoolFactory DescriptorPoolFactory { get; }
        public LogicalDeviceWrapper Device { get; }
        public InstanceWrapper Instance { get;}
        public ISurfaceHandler Surface { get; }

        public CommandPoolWrapper GetOrCreateCommandPool();
    }
}