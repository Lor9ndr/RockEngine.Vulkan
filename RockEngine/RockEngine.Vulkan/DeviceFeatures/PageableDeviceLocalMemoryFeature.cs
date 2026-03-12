using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    public class PageableDeviceLocalMemoryFeature : DeviceFeature
    {
        public PageableDeviceLocalMemoryFeature() :base("Pageable Device Local Memory")
        {
                
        }

        public override bool IsSupported(VkPhysicalDevice physicalDevice)
            => physicalDevice.IsExtensionPresent("VK_EXT_pageable_device_local_memory");

        public override void Enable(ref PhysicalDeviceFeatures2 features2,
              ref PhysicalDeviceVulkan11Features vk11,
              ref PhysicalDeviceVulkan12Features vk12,
              ref PhysicalDeviceVulkan13Features vk13,
              ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
              Chain chain)
        {
            pageableDeviceLocalMemoryFeatures.PageableDeviceLocalMemory = true;
           
        }

        public override IEnumerable<string> GetRequiredExtensions()
            => ["VK_EXT_pageable_device_local_memory"];

        public override IEnumerable<string> GetPreprocessorDefines()
            => ["PAGEABLE_DEVICE_LOCAL_MEMORY_SUPPORTED"];
    }
}
