using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkFramebuffer : VkObject<Framebuffer>
    {
        private readonly RenderingContext _context;

        private VkFramebuffer(RenderingContext context, in Framebuffer framebuffer)
            : base(framebuffer)
        {
            _context = context;
        }

        public static unsafe VkFramebuffer Create(RenderingContext context, in FramebufferCreateInfo framebufferCreateInfo)
        {
            RenderingContext.Vk.CreateFramebuffer(context.Device, in framebufferCreateInfo, in RenderingContext.CustomAllocator<VkFramebuffer>(), out Framebuffer framebuffer)
                    .VkAssertResult("Failed to create framebuffer.");

            return new VkFramebuffer(context, framebuffer);
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
                    RenderingContext.Vk.DestroyFramebuffer(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkFramebuffer>());
                }

                _disposed = true;
            }
        }
    }
}