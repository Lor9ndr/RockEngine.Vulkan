using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    /// <summary>
    /// Enables bindless texture support via descriptor indexing.
    /// Required for shaders using <c>nonuniformEXT</c> and variable-sized texture arrays.
    /// </summary>
    public class DescriptorIndexingFeature : DeviceFeature
    {
        public DescriptorIndexingFeature() : base("DescriptorIndexing")
        {
        }

        /// <summary>
        /// Checks if the physical device supports all required features for bindless textures.
        /// </summary>
        public unsafe override bool IsSupported(VkPhysicalDevice physicalDevice)
        {
            // Check the essential features
            return physicalDevice.Features12.RuntimeDescriptorArray &&
                   physicalDevice.Features12.ShaderSampledImageArrayNonUniformIndexing &&
                   physicalDevice.Features12.DescriptorBindingVariableDescriptorCount;
        }

        /// <summary>
        /// Enables the required Vulkan 1.2 features for bindless textures.
        /// </summary>
        public override void Enable(
            ref PhysicalDeviceFeatures2 features2,
            ref PhysicalDeviceVulkan11Features vk11,
            ref PhysicalDeviceVulkan12Features vk12,
            ref PhysicalDeviceVulkan13Features vk13,
            ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
            Chain chain)
        {
            // Required features
            vk12.DescriptorIndexing = Vk.True;
            vk12.RuntimeDescriptorArray = Vk.True;
            vk12.ShaderSampledImageArrayNonUniformIndexing = Vk.True;
            vk12.DescriptorBindingVariableDescriptorCount = Vk.True;

            // Strongly recommended for flexibility
            vk12.DescriptorBindingPartiallyBound = Vk.True;
            vk12.DescriptorBindingSampledImageUpdateAfterBind = Vk.True;
        }

        /// <summary>
        /// Returns the required extension for descriptor indexing.
        /// </summary>
        public override IEnumerable<string> GetRequiredExtensions()
        {
            // VK_EXT_descriptor_indexing is required even on Vulkan 1.2+ for some features
            yield return "VK_EXT_descriptor_indexing";
        }

        /// <summary>
        /// Returns the preprocessor define used by shaders to activate bindless code paths.
        /// </summary>
        public override IEnumerable<string> GetPreprocessorDefines()
        {
            yield return "BINDLESS_SUPPORTED";
        }

        public override IEnumerable<string> GetShaderExtensionsToEnable()
        {
            yield return "GL_EXT_nonuniform_qualifier : require";
        }
    }
}