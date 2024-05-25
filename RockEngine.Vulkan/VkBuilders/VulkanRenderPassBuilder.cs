using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanRenderPassBuilder
    {
        private readonly List<AttachmentDescription> _attachments = new List<AttachmentDescription>();
        private readonly List<SubpassDescription> _subpasses = new List<SubpassDescription>();
        private readonly List<SubpassDependency> _dependencies = new List<SubpassDependency>();

        private readonly VulkanContext _context;

        public VulkanRenderPassBuilder(VulkanContext context)
        {
            _context = context;
        }

        public VulkanRenderPassBuilder AddAttachment(AttachmentDescription attachment)
        {
            _attachments.Add(attachment);
            return this;
        }

        public VulkanRenderPassBuilder AddSubpass(SubpassDescription subpass)
        {
            _subpasses.Add(subpass);
            return this;
        }

        public VulkanRenderPassBuilder AddDependency(SubpassDependency dependency)
        {
            _dependencies.Add(dependency);
            return this;
        }


        public VulkanRenderPass Build()
        {
            RenderPass renderPass;
            unsafe
            {
                fixed (SubpassDescription* subpass = _subpasses.ToArray())
                {
                    fixed(AttachmentDescription* attachment = _attachments.ToArray())
                    {
                        fixed(SubpassDependency* dependency = _dependencies.ToArray())
                        {
                            RenderPassCreateInfo renderPassInfo = new RenderPassCreateInfo
                            {
                                SType = StructureType.RenderPassCreateInfo,
                                AttachmentCount = (uint)_attachments.Count,
                                PAttachments = attachment,
                                SubpassCount = (uint)_subpasses.Count,
                                PSubpasses = subpass,
                                DependencyCount = (uint)_dependencies.Count,
                                PDependencies = dependency
                            };
                            _context.Api.CreateRenderPass(_context.Device.Device, in renderPassInfo, null, out renderPass)
                                .ThrowCode("Failed to create render pass.");
                        }
                    }
                }
            }

            return new VulkanRenderPass(_context, renderPass);
        }
    }
}