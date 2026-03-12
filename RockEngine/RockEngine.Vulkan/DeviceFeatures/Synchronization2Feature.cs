using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    public class Synchronization2Feature : DeviceFeature
    {
        public Synchronization2Feature() : base("Synchronization2")
        {

        }

        public override bool IsSupported(VkPhysicalDevice physicalDevice)
            => physicalDevice.Features13.Synchronization2;

        public override void Enable(ref PhysicalDeviceFeatures2 features2,
              ref PhysicalDeviceVulkan11Features vk11,
              ref PhysicalDeviceVulkan12Features vk12,
              ref PhysicalDeviceVulkan13Features vk13,
              ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
              Chain chain)
        {
            vk13.Synchronization2 = true;
        }

        public override IEnumerable<string> GetRequiredExtensions() => [];
        public override IEnumerable<string> GetPreprocessorDefines() => [];
    }
}
