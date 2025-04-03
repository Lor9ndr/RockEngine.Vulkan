
using Silk.NET.Vulkan;
namespace RockEngine.Vulkan
{
    public record VkRenderPass : VkObject<RenderPass>
    {
        private readonly VulkanContext _context;

        public VkRenderPass(VulkanContext context, RenderPass renderPass)
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
                    VulkanContext.Vk.DestroyRenderPass(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkRenderPass>());
                }

                _disposed = true;
            }
        }
        public static unsafe VkRenderPass Create(VulkanContext context, in RenderPassCreateInfo createInfo)
        {
            VulkanContext.Vk.CreateRenderPass(context.Device, in createInfo, in VulkanContext.CustomAllocator<VkRenderPass>(), out RenderPass renderPass)
                     .VkAssertResult("Failed to create render pass.");

            return new VkRenderPass(context, renderPass);
        }
        public static unsafe VkRenderPass Create(VulkanContext context, SubpassDescription[] subpasses, AttachmentDescription[] attachments, SubpassDependency[] dependencies)
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

                VulkanContext.Vk.CreateRenderPass(context.Device, in createInfo, in VulkanContext.CustomAllocator<VkRenderPass>(), out renderPass)
                    .VkAssertResult("Failed to create render pass.");
            }

            return new VkRenderPass(context, renderPass);
        }
    }
}