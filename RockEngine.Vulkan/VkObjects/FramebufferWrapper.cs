using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class FramebufferWrapper : VkObject<Framebuffer>
    {
        private readonly VulkanContext _context;

        private FramebufferWrapper(VulkanContext context, in Framebuffer framebuffer)
            :base(framebuffer)
        {
            _context = context;
        }

        public static unsafe FramebufferWrapper Create(VulkanContext context, in FramebufferCreateInfo framebufferCreateInfo)
        {
            context.Api.CreateFramebuffer(context.Device, in framebufferCreateInfo, null, out Framebuffer framebuffer)
                    .ThrowCode("Failed to create framebuffer.");

            return new FramebufferWrapper(context, framebuffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                unsafe
                {
                    _context.Api.DestroyFramebuffer(_context.Device, _vkObject, null);
                }

                _disposed = true;
            }
        }
    }
}