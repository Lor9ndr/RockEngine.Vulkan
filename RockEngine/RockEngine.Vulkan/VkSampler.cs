using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkSampler : VkObject<Sampler>
    {
        private readonly VulkanContext _context;

        public VkSampler(VulkanContext context, in Sampler vkObject)
            : base(vkObject)
        {
            _context = context;
        }

        public static unsafe VkSampler Create(VulkanContext context, in SamplerCreateInfo ci)
        {
            VK.CreateSampler(context.Device, in ci, in CustomAllocator<VkSampler>(), out var sampler)
                 .VkAssertResult("Failed to create sampler");
            return new VkSampler(context, sampler);
        }

        protected override unsafe void Dispose(bool disposing)
        {
            VK.DestroySampler(_context.Device, _vkObject, in CustomAllocator<VkSampler>());
        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.Sampler, name);

    }
}
