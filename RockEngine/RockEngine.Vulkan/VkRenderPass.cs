
using Silk.NET.Vulkan;
namespace RockEngine.Vulkan
{
    public record VkRenderPass : VkObject<RenderPass>
    {
        private readonly RenderingContext _context;

        public VkRenderPass(RenderingContext context, RenderPass renderPass)
            : base(renderPass)
        {
            _context = context;
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
                    RenderingContext.Vk.DestroyRenderPass(_context.Device, _vkObject, in RenderingContext.CustomAllocator);
                }

                _disposed = true;
            }
        }
        public unsafe static VkRenderPass Create(RenderingContext context, in RenderPassCreateInfo createInfo)
        {
            RenderingContext.Vk.CreateRenderPass(context.Device, in createInfo, in RenderingContext.CustomAllocator, out RenderPass renderPass)
                     .VkAssertResult("Failed to create render pass.");

            return new VkRenderPass(context, renderPass);
        }
        public unsafe static VkRenderPass Create(RenderingContext context, SubpassDescription[] subpasses, AttachmentDescription[] attachments, SubpassDependency[] dependencies)
        {
            RenderPass renderPass;
            fixed (SubpassDescription* pSubpasses = subpasses)
            fixed (AttachmentDescription* pAttachments = attachments)
            fixed (SubpassDependency* pDependencies = dependencies)
            {
                var createInfo = new RenderPassCreateInfo
                {
                    SType = StructureType.RenderPassCreateInfo,
                    PNext = null,
                    Flags = 0,
                    AttachmentCount = (uint)attachments.Length,
                    PAttachments = pAttachments,
                    SubpassCount = (uint)subpasses.Length,
                    PSubpasses = pSubpasses,
                    DependencyCount = (uint)dependencies.Length,
                    PDependencies = pDependencies
                };

                RenderingContext.Vk.CreateRenderPass(context.Device, in createInfo, in RenderingContext.CustomAllocator, out renderPass)
                    .VkAssertResult("Failed to create render pass.");
            }

            return new VkRenderPass(context, renderPass);
        }
    }
}