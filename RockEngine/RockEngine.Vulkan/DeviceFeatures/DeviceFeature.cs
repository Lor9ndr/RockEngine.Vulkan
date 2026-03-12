using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.DeviceFeatures
{
    public abstract class DeviceFeature
    {
        public string Name { get; }
        public virtual bool IsRequired { get; init;  }

        public DeviceFeature(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Returns true if this feature is supported by the physical device.
        /// </summary>
        public abstract bool IsSupported(VkPhysicalDevice physicalDevice);

        /// <summary>
        /// Enables the feature by adding the appropriate structures to the feature chain.
        /// </summary>
        public abstract void Enable(
              ref PhysicalDeviceFeatures2 features2,
              ref PhysicalDeviceVulkan11Features vk11,
              ref PhysicalDeviceVulkan12Features vk12,
              ref PhysicalDeviceVulkan13Features vk13,
              ref PhysicalDevicePageableDeviceLocalMemoryFeaturesEXT pageableDeviceLocalMemoryFeatures,
              Chain chain);

        /// <summary>
        /// Returns the list of extension names required for this feature.
        /// </summary>
        public abstract IEnumerable<string> GetRequiredExtensions();

        /// <summary>
        /// Returns preprocessor defines to pass to shader compiler when feature is enabled.
        /// </summary>
        public abstract IEnumerable<string> GetPreprocessorDefines();
    }
}