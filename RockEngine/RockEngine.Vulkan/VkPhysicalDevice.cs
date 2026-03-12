using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkPhysicalDevice : VkObject<PhysicalDevice>
    {
        private readonly VkInstance _instance;

        public PhysicalDeviceProperties Properties { get; }
        public PhysicalDeviceFeatures Features { get; }
        public PhysicalDeviceFeatures2 Features2 { get; }
        public PhysicalDeviceVulkan11Features Features11 { get; }
        public PhysicalDeviceVulkan12Features Features12 { get; }
        public PhysicalDeviceVulkan13Features Features13 { get; }

        public bool SupportsMultiDrawIndirect => Features2.Features.MultiDrawIndirect == Vk.True;

        private VkPhysicalDevice(
            VkInstance instance,
            PhysicalDevice physicalDevice,
            PhysicalDeviceProperties properties,
            PhysicalDeviceFeatures features,
            PhysicalDeviceFeatures2 features2,
            PhysicalDeviceVulkan11Features features11,
            PhysicalDeviceVulkan12Features features12,
            PhysicalDeviceVulkan13Features features13)
            : base(physicalDevice)
        {
            _instance = instance;
            Properties = properties;
            Features = features;
            Features2 = features2;
            Features11 = features11;
            Features12 = features12;
            Features13 = features13;
        }

        public static unsafe VkPhysicalDevice Create(VkInstance instance)
        {
            uint count = 0;
            VulkanContext.Vk.EnumeratePhysicalDevices(instance, ref count, null);
            if (count == 0)
                throw new Exception("Failed to find GPUs with Vulkan support.");

            Span<PhysicalDevice> devices = stackalloc PhysicalDevice[(int)count];
            VulkanContext.Vk.EnumeratePhysicalDevices(instance, &count, devices);

            PhysicalDevice selectedDevice = devices[0];
            var properties = VulkanContext.Vk.GetPhysicalDeviceProperties(selectedDevice);
            var features = VulkanContext.Vk.GetPhysicalDeviceFeatures(selectedDevice);

            // Build a feature chain to get 1.1, 1.2, 1.3 features
            var features2 = new PhysicalDeviceFeatures2
            {
                SType = StructureType.PhysicalDeviceFeatures2
            };
            var features11 = new PhysicalDeviceVulkan11Features
            {
                SType = StructureType.PhysicalDeviceVulkan11Features
            };
            var features12 = new PhysicalDeviceVulkan12Features
            {
                SType = StructureType.PhysicalDeviceVulkan12Features
            };
            var features13 = new PhysicalDeviceVulkan13Features
            {
                SType = StructureType.PhysicalDeviceVulkan13Features
            };

            // Chain them: features2 -> features11 -> features12 -> features13
            features2.PNext = &features11;
            features11.PNext = &features12;
            features12.PNext = &features13;

            VulkanContext.Vk.GetPhysicalDeviceFeatures2(selectedDevice, &features2);

            return new VkPhysicalDevice(
                instance,
                selectedDevice,
                properties,
                features,
                features2,
                features11,
                features12,
                features13);
        }

        public FormatProperties GetFormatProperties(Format format)
        {
            return VulkanContext.Vk.GetPhysicalDeviceFormatProperties(this, format);
        }

        public PhysicalDeviceFeatures GetPhysicalDeviceFeatures()
        {
            return VulkanContext.Vk.GetPhysicalDeviceFeatures(this);
        }
        public bool IsExtensionPresent(string extension)
        {
            return Vk.IsDeviceExtensionPresent(_instance, extension);
        }
        public Format FindDepthFormat() => FindSupportedFormat(
       [Format.D24UnormS8Uint, Format.D32Sfloat, Format.D32SfloatS8Uint], // Prefer D24S8 first
       ImageTiling.Optimal,
       FormatFeatureFlags.DepthStencilAttachmentBit
   );

        private Format FindSupportedFormat(Format[] candidates, ImageTiling tiling, FormatFeatureFlags features)
        {
            foreach (var format in candidates)
            {
                var properties = GetFormatProperties(format);

                switch (tiling)
                {
                    case ImageTiling.Linear when (properties.LinearTilingFeatures & features) == features:
                        return format;
                    case ImageTiling.Optimal when (properties.OptimalTilingFeatures & features) == features:
                        return format;
                }
            }
            throw new InvalidOperationException("Failed to find supported format.");
        }
        public FormatFeatureFlags GetFormatFeatures(Format format)
        {
            var props = GetFormatProperties(format);
            return props.OptimalTilingFeatures;
        }
        public override void LabelObject(string name) { }

        protected override void Dispose(bool disposing)
        {
        }


    }
}