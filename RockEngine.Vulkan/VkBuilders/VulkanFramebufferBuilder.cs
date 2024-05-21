using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanFramebufferBuilder : DisposableBuilder
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private List<ImageView> _attachments = new List<ImageView>();
        private VulkanRenderPass _renderPass;
        private uint _width;
        private uint _height;
        private uint _layersCount;

        public VulkanFramebufferBuilder(Vk api, VulkanLogicalDevice device)
        {
            _api = api;
            _device = device;
        }

        public VulkanFramebufferBuilder AddAttachment(ImageView view)
        {
            _attachments.Add(view);
            return this;
        }

        public VulkanFramebufferBuilder WithRenderPass(VulkanRenderPass renderPass)
        {
            _renderPass = renderPass;
            return this;
        }

        public VulkanFramebufferBuilder WithWidth(uint width)
        {
            _width = width;
            return this;
        }

        public VulkanFramebufferBuilder WithHeight(uint height)
        {
            _height = height;
            return this;
        }

        public VulkanFramebufferBuilder WithLayersCount(uint count)
        {
            _layersCount = count;
            return this;
        }
        public unsafe VulkanFramebuffer Build()
        {
            FramebufferCreateInfo ci = new FramebufferCreateInfo()
            {
                SType = StructureType.FramebufferCreateInfo,
                AttachmentCount = (uint)_attachments.Count,
                PAttachments = (ImageView*)CreateMemoryHandle(_attachments.ToArray()).Pointer,
                RenderPass = _renderPass.RenderPass,
                Width = _width,
                Height = _height,
                Layers = _layersCount
            };
            _api.CreateFramebuffer(_device.Device, in ci, null, out Framebuffer framebuffer)
                .ThrowCode("Failed to create framebuffer");
            return new VulkanFramebuffer(_api, _device, framebuffer);
        }
    }
}
