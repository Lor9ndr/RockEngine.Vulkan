using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkFrameBuffer : VkObject<Framebuffer>
    {
        private readonly RenderingContext _context;

        public VkImageView[] ColorAttachmentViews { get; }
        public VkSampler Sampler { get; }
        private VkFrameBuffer(RenderingContext context,  in Framebuffer framebuffer, VkImageView[] attachments)
            : base(framebuffer)
        {
            _context = context;
            ColorAttachmentViews = attachments;
            var samplerInfo = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
            };
            Sampler = VkSampler.Create(context, samplerInfo);
        }

        public static unsafe VkFrameBuffer Create(RenderingContext context, in FramebufferCreateInfo framebufferCreateInfo, VkImageView[] attachments)
        {
            RenderingContext.Vk.CreateFramebuffer(context.Device, in framebufferCreateInfo, in RenderingContext.CustomAllocator<VkFrameBuffer>(), out Framebuffer framebuffer)
                    .VkAssertResult("Failed to create framebuffer.");

            return new VkFrameBuffer(context, framebuffer, attachments);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                    foreach (var attachment in ColorAttachmentViews)
                    {
                        attachment.Dispose();
                    }
                    Sampler.Dispose();
                }

                unsafe
                {
                    RenderingContext.Vk.DestroyFramebuffer(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkFrameBuffer>());
                }

                _disposed = true;
            }
        }
    }
}
