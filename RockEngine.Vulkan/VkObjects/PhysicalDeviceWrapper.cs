using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public partial class PhysicalDeviceWrapper : VkObject<PhysicalDevice>
    {
        private readonly VulkanContext _context;


        public PhysicalDeviceWrapper(VulkanContext context, PhysicalDevice physicalDevice)
            :base(physicalDevice)
        {
            _context = context;
        }
        public static PhysicalDeviceWrapper Create(VulkanContext context)
        {
            Span<PhysicalDevice> empty = new Span<PhysicalDevice>();
            Span<uint> cnt = stackalloc uint[1];
            context.Api.EnumeratePhysicalDevices(context.Instance, cnt, empty);
            if (cnt[0] == 0)
            {
                throw new Exception("Failed to find GPUs with Vulkan support.");
            }
            Span<PhysicalDevice> devices = stackalloc PhysicalDevice[(int)cnt[0]];
            context.Api.EnumeratePhysicalDevices(context.Instance, cnt, devices);
            // Example criteria for selecting a physical device could be added here
            // For simplicity, just select the first device
            PhysicalDevice selectedDevice = devices[0];
            return new PhysicalDeviceWrapper(context, selectedDevice);
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}