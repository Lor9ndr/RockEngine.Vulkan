using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkPhysicalDevice : VkObject<PhysicalDevice>
    {
        public PhysicalDeviceProperties Properties { get; }

        public VkPhysicalDevice( PhysicalDevice physicalDevice, PhysicalDeviceProperties properties)
            : base(physicalDevice)
        {
            Properties = properties;
        }
        public static VkPhysicalDevice Create(VkInstance instance)
        {
            Span<PhysicalDevice> empty = Span<PhysicalDevice>.Empty;
            Span<uint> cnt = stackalloc uint[1];
            RenderingContext.Vk.EnumeratePhysicalDevices(instance, cnt, empty);
            if (cnt[0] == 0)
            {
                throw new Exception("Failed to find GPUs with Vulkan support.");
            }
            Span<PhysicalDevice> devices = stackalloc PhysicalDevice[(int)cnt[0]];
            RenderingContext.Vk.EnumeratePhysicalDevices(instance, cnt, devices);

            PhysicalDevice selectedDevice = devices[0];
            var properties = RenderingContext.Vk.GetPhysicalDeviceProperties(selectedDevice);
            return new VkPhysicalDevice(selectedDevice, properties);
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}