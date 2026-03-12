using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    public class ShaderDrawParametersFeature : DeviceFeature
    {
        public ShaderDrawParametersFeature() : base("Shader Draw Parameters")
        {
        }

        public override bool IsSupported(VkPhysicalDevice physicalDevice)
            => physicalDevice.Features11.ShaderDrawParameters;

        public override void Enable(ref PhysicalDeviceFeatures2 features2,
              ref PhysicalDeviceVulkan11Features vk11,
              ref PhysicalDeviceVulkan12Features vk12,
              ref PhysicalDeviceVulkan13Features vk13,
              ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
              Chain chain)
            => vk11.ShaderDrawParameters = true;

        public override IEnumerable<string> GetRequiredExtensions() => [];
        public override IEnumerable<string> GetPreprocessorDefines() => [];
    }
}
