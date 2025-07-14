using Silk.NET.Vulkan;

using System.Net.Mail;

namespace RockEngine.Vulkan
{
    public class VkFrameBuffer : VkObject<Framebuffer>
    {
        private readonly VulkanContext _context;
        private VkImageView[] _attachments;
        private FramebufferCreateInfo _framebufferCreateInfo;

        private uint _width;
        private uint _height;

        public VkImageView[] ColorAttachmentViews => _attachments; 
        public VkSampler Sampler { get; }
        private VkFrameBuffer(VulkanContext context, in Framebuffer framebuffer, VkImageView[] attachments, in FramebufferCreateInfo framebufferCreateInfo)
            : base(framebuffer)
        {
            _context = context;
            _attachments = attachments;
            _framebufferCreateInfo = framebufferCreateInfo;

            var samplerInfo = new SamplerCreateInfo()
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
            };
            Sampler = context.SamplerCache.GetSampler(samplerInfo);
        }

        public static unsafe VkFrameBuffer Create(VulkanContext context, in FramebufferCreateInfo framebufferCreateInfo, VkImageView[] attachments)
        {
            VulkanContext.Vk.CreateFramebuffer(context.Device, in framebufferCreateInfo, in VulkanContext.CustomAllocator<VkFrameBuffer>(), out Framebuffer framebuffer)
                    .VkAssertResult("Failed to create framebuffer.");

            return new VkFrameBuffer(context, framebuffer, attachments, in framebufferCreateInfo);
        }

        private unsafe Framebuffer CreateFramebufferInternal()
        {
            fixed (ImageView* attachmentsPtr = _attachments.Select(a => a.VkObjectNative).ToArray())
            {
                _framebufferCreateInfo.PAttachments = attachmentsPtr;
                _framebufferCreateInfo.AttachmentCount = (uint)_attachments.Length;
                _framebufferCreateInfo.Width = _width;
                _framebufferCreateInfo.Height = _height;

                VulkanContext.Vk.CreateFramebuffer(_context.Device, in _framebufferCreateInfo, null, out Framebuffer fb)
                    .VkAssertResult("Failed to recreate framebuffer");
                return fb;
            }
        }

        public unsafe static VkFrameBuffer Create(VulkanContext context, VkRenderPass renderPass, VkImageView[] attachments, uint width, uint height)
        {
            fixed(ImageView* pAttachments = attachments.Select(s => s.VkObjectNative).ToArray())
            {
                FramebufferCreateInfo ci = new FramebufferCreateInfo()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    Height = height,
                    Width = width,
                    AttachmentCount = (uint)attachments.Length,
                    Layers = 1,
                    PAttachments = pAttachments,
                    RenderPass = renderPass,
                    Flags = FramebufferCreateFlags.None,
                };
                return Create(context, ci, attachments);
            }
        }

        public void Recreate(VkImageView[] attachments, uint width, uint height)
        {
            DisposeInternal();
            _attachments = attachments;
            _width = width;
            _height = height;
            var newFbo = CreateFramebufferInternal();
            _vkObject = newFbo;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                DisposeInternal();

                _disposed = true;
            }
        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.Framebuffer, name);

        private void DisposeInternal()
        {
           VulkanContext.Vk.DestroyFramebuffer(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkFrameBuffer>());
        }

    }
}
