using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanColorBlendAttachmentBuilder
    {
        public PipelineColorBlendAttachmentState Build(bool blendEnable, ColorComponentFlags flags)
        {
            return new PipelineColorBlendAttachmentState()
            {
                BlendEnable = blendEnable,
                ColorWriteMask = flags,
            };
        }
    }
}
