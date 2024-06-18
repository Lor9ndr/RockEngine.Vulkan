using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class Sampler : VkObject<Silk.NET.Vulkan.Sampler>
    {
        private readonly VulkanContext _context;

        public Sampler(VulkanContext context, in Silk.NET.Vulkan.Sampler vkObject)
            : base(vkObject)
        {
            _context = context;
        }

        public unsafe static Sampler Create(VulkanContext context, in SamplerCreateInfo ci)
        {
            context.Api.CreateSampler(context.Device, in ci, null, out var sampler)
                 .ThrowCode("Failed to create sampler");
            return new Sampler(context, sampler);
        }

        protected override unsafe void Dispose(bool disposing)
        {
            _context.Api.DestroySampler(_context.Device, _vkObject, null);
        }
    }
}
