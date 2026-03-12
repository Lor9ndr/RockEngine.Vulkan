using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    public class MultiDrawIndirectFeature : DeviceFeature
    {
        public MultiDrawIndirectFeature() : base("MultiDraw Indirect")
        {

        }

        public override bool IsSupported(VkPhysicalDevice physicalDevice)
            => physicalDevice.Features2.Features.MultiDrawIndirect;

        public override void Enable(ref PhysicalDeviceFeatures2 features2,
              ref PhysicalDeviceVulkan11Features vk11,
              ref PhysicalDeviceVulkan12Features vk12,
              ref PhysicalDeviceVulkan13Features vk13,
              ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
              Chain chain)
            => features2.Features.MultiDrawIndirect = true;

        public override IEnumerable<string> GetRequiredExtensions() => [];
        public override IEnumerable<string> GetPreprocessorDefines() => [];
    }
}
