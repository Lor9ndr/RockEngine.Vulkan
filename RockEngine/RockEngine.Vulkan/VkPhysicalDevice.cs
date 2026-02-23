using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkPhysicalDevice : VkObject<PhysicalDevice>
    {
        public PhysicalDeviceProperties Properties { get; }
        public PhysicalDeviceFeatures Features { get; }
        public PhysicalDeviceFeatures2 Features2 { get; }
        public bool SupportsMultiDrawIndirect =>
   Features2.Features.MultiDrawIndirect == Vk.True;

        public VkPhysicalDevice(PhysicalDevice physicalDevice,
                              PhysicalDeviceProperties properties,
                              PhysicalDeviceFeatures features,
                              PhysicalDeviceFeatures2 features2)
            : base(physicalDevice)
        {
            Properties = properties;
            Features = features;
            Features2 = features2;
        }
        public static VkPhysicalDevice Create(VkInstance instance)
        {
            Span<PhysicalDevice> empty = [];
            Span<uint> cnt = stackalloc uint[1];
            VulkanContext.Vk.EnumeratePhysicalDevices(instance, cnt, empty);
            if (cnt[0] == 0)
            {
                throw new Exception("Failed to find GPUs with Vulkan support.");
            }
            Span<PhysicalDevice> devices = stackalloc PhysicalDevice[(int)cnt[0]];
            VulkanContext.Vk.EnumeratePhysicalDevices(instance, cnt, devices);

            PhysicalDevice selectedDevice = devices[0];
            var properties = VulkanContext.Vk.GetPhysicalDeviceProperties(selectedDevice);
            var features = VulkanContext.Vk.GetPhysicalDeviceFeature(selectedDevice);
            VulkanContext.Vk.GetPhysicalDeviceFeatures2(selectedDevice, out PhysicalDeviceFeatures2 features2);
            return new VkPhysicalDevice(selectedDevice, properties, features, features2);
        }


        public FormatProperties GetFormatProperties(Format format)
        {
            return VulkanContext.Vk.GetPhysicalDeviceFormatProperties(this, format);
        }

        public PhysicalDeviceFeatures GetPhysicalDeviceFeatures()
        {
            return VulkanContext.Vk.GetPhysicalDeviceFeatures(this);

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