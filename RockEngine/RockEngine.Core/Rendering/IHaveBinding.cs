using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public interface IHaveBinding
    {
        public Dictionary<DescriptorSetLayout, DescriptorSet> Bindings { get; set;}

        public void UpdateSet(DescriptorSet set, DescriptorSetLayout setLayout, uint binding, uint dstArrayElement = 0);

        public void Use(VkCommandBuffer commandBuffer, VkPipeline pipeline, uint dynamicOffset = 0, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics);
    }
}
