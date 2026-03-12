using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    public class SamplerAnisotropyFeature : DeviceFeature
    {
        public SamplerAnisotropyFeature() : base("Sampler Anisotropy")
        {

        }

        public override bool IsSupported(VkPhysicalDevice physicalDevice)
            => physicalDevice.Features.SamplerAnisotropy;

        public override void Enable(ref PhysicalDeviceFeatures2 features2,
              ref PhysicalDeviceVulkan11Features vk11,
              ref PhysicalDeviceVulkan12Features vk12,
              ref PhysicalDeviceVulkan13Features vk13,
              ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
              Chain chain)
            => features2.Features.SamplerAnisotropy = true;

        public override IEnumerable<string> GetRequiredExtensions() => [];
        public override IEnumerable<string> GetPreprocessorDefines() => [];
    }
}
