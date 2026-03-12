using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    public class HostQueryResetFeature : DeviceFeature
    {
        public HostQueryResetFeature() :base("Host Query Reset")
        {

        }

        public override bool IsSupported(VkPhysicalDevice physicalDevice)
            => physicalDevice.Features12.HostQueryReset;

        public override void Enable(ref PhysicalDeviceFeatures2 features2,
              ref PhysicalDeviceVulkan11Features vk11,
              ref PhysicalDeviceVulkan12Features vk12,
              ref PhysicalDeviceVulkan13Features vk13,
              ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
              Chain chain)
            => vk12.HostQueryReset = true;

        public override IEnumerable<string> GetRequiredExtensions() => [];
        public override IEnumerable<string> GetPreprocessorDefines() => [];
    }
}
