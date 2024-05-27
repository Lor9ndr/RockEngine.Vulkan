using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanPhysicalDeviceBuilder
    {
        private readonly Vk _api;
        private readonly Instance _instance;

        public VulkanPhysicalDeviceBuilder(Vk api, Instance instance)
        {
            _api = api;
            _instance = instance;
        }

        public PhysicalDeviceWrapper Build()
        {
            Span<PhysicalDevice> empty = new Span<PhysicalDevice>();
            Span<uint> cnt = stackalloc uint[1];
            _api.EnumeratePhysicalDevices(_instance, cnt, empty);
            if (cnt[0] == 0)
            {
                throw new Exception("Failed to find GPUs with Vulkan support.");
            }
            Span<PhysicalDevice> devices = stackalloc PhysicalDevice[(int)cnt[0]];
            _api.EnumeratePhysicalDevices(_instance, cnt, devices);
            // Example criteria for selecting a physical device could be added here
            // For simplicity, just select the first device
            PhysicalDevice selectedDevice = devices[0];

            return new PhysicalDeviceWrapper(selectedDevice, _api);
        }
    }
}
