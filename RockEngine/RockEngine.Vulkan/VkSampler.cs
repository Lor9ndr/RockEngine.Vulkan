using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkSampler : VkObject<Sampler>
    {
        private readonly RenderingContext _context;

        public VkSampler(RenderingContext context, in Sampler vkObject)
            : base(vkObject)
        {
            _context = context;
        }

        public unsafe static VkSampler Create(RenderingContext context, in SamplerCreateInfo ci)
        {
            RenderingContext.Vk.CreateSampler(context.Device, in ci, in RenderingContext.CustomAllocator<VkSampler>(), out var sampler)
                 .VkAssertResult("Failed to create sampler");
            return new VkSampler(context, sampler);
        }

        protected override unsafe void Dispose(bool disposing)
        {
            RenderingContext.Vk.DestroySampler(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkSampler>());
        }
    }
}
