using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkFrameBuffer : VkObject<Framebuffer>
    {
        private readonly VulkanContext _context;

        public VkImageView[] ColorAttachmentViews { get; }
        public VkSampler Sampler { get; }
        private VkFrameBuffer(VulkanContext context, in Framebuffer framebuffer, VkImageView[] attachments)
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

        public static unsafe VkFrameBuffer Create(VulkanContext context, in FramebufferCreateInfo framebufferCreateInfo, VkImageView[] attachments)
        {
            VulkanContext.Vk.CreateFramebuffer(context.Device, in framebufferCreateInfo, in VulkanContext.CustomAllocator<VkFrameBuffer>(), out Framebuffer framebuffer)
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
                    VulkanContext.Vk.DestroyFramebuffer(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkFrameBuffer>());
                }

                _disposed = true;
            }
        }
    }
}
