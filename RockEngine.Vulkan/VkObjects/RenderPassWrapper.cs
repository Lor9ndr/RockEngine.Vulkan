using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;
namespace RockEngine.Vulkan.VkObjects
{
    public class RenderPassWrapper : VkObject<RenderPass>
    {
        private readonly VulkanContext _context;

        public RenderPassWrapper(VulkanContext context, RenderPass renderPass)
            :base(renderPass)
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
                    _context.Api.DestroyRenderPass(_context.Device, _vkObject, null);
                }

                _disposed = true;
            }
        }
        public unsafe static RenderPassWrapper Create(VulkanContext context, in RenderPassCreateInfo createInfo)
        {
            context.Api.CreateRenderPass(context.Device, in createInfo, null, out RenderPass renderPass)
                     .ThrowCode("Failed to create render pass.");

            return new RenderPassWrapper(context, renderPass);
        }
        public unsafe static RenderPassWrapper Create(VulkanContext context, SubpassDescription[] subpasses, AttachmentDescription[] attachments, SubpassDependency[] dependencies)
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

                context.Api.CreateRenderPass(context.Device, in createInfo, null, out renderPass)
                    .ThrowCode("Failed to create render pass.");
            }

            return new RenderPassWrapper(context, renderPass);
        }
    }
}