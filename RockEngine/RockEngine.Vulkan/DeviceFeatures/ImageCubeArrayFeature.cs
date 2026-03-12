using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    public class ImageCubeArrayFeature : DeviceFeature
    {
        public ImageCubeArrayFeature() : base("ImageCubeArray Indirect")
        {

        }

        public override bool IsSupported(VkPhysicalDevice physicalDevice)
            => physicalDevice.Features2.Features.ImageCubeArray;

        public override void Enable(ref PhysicalDeviceFeatures2 features2,
              ref PhysicalDeviceVulkan11Features vk11,
              ref PhysicalDeviceVulkan12Features vk12,
              ref PhysicalDeviceVulkan13Features vk13,
              ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
              Chain chain)
            => features2.Features.ImageCubeArray = true;

        public override IEnumerable<string> GetRequiredExtensions() => [];
        public override IEnumerable<string> GetPreprocessorDefines() => [];
    }
}
