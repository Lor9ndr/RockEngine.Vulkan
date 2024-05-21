using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanPipelineLayoutCreateBuilder
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;

        public VulkanPipelineLayoutCreateBuilder(Vk api, VulkanLogicalDevice device)
        {
            _api = api;
            _device = device;
        }

        public VulkanPipelineLayout Build()
        {
            PipelineLayoutCreateInfo pipelineLayoutCreateInfo = new PipelineLayoutCreateInfo()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
            };
            unsafe
            {
                _api.CreatePipelineLayout(_device.Device, ref pipelineLayoutCreateInfo, null, out var pipelineLayout)
                    .ThrowCode("Failed to create pipeline layout");
                return new VulkanPipelineLayout(_api, pipelineLayout, _device);
            }
        }
    }
}
