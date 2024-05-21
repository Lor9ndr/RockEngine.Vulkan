using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanRenderPassBuilder
    {
        private readonly Vk _vk;
        private readonly VulkanLogicalDevice _device;
        private List<AttachmentDescription> _attachments = new List<AttachmentDescription>();
        private List<SubpassDescription> _subpasses = new List<SubpassDescription>();
        private List<SubpassDependency> _dependencies = new List<SubpassDependency>();

        public VulkanRenderPassBuilder(Vk vk, VulkanLogicalDevice device)
        {
            _vk = vk;
            _device = device;
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
                            _vk.CreateRenderPass(_device.Device, in renderPassInfo, null, out renderPass)
                                .ThrowCode("Failed to create render pass.");
                        }
                    }
                }
            }

            return new VulkanRenderPass(_vk, _device, renderPass);
        }
    }
}