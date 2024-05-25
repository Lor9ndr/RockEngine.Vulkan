using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanPipelineLayoutCreateBuilder
    {

        public VulkanPipelineLayout Build(VulkanContext context)
        {
            PipelineLayoutCreateInfo pipelineLayoutCreateInfo = new PipelineLayoutCreateInfo()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
            };
            unsafe
            {
                context.Api.CreatePipelineLayout(context.Device.Device, ref pipelineLayoutCreateInfo, null, out var pipelineLayout)
                    .ThrowCode("Failed to create pipeline layout");
                return new VulkanPipelineLayout(context, pipelineLayout);
            }
        }
    }
}
