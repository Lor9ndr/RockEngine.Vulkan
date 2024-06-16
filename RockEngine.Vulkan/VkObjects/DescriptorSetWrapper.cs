using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public struct DescriptorSetLayoutWrapper
    {
        public DescriptorSetLayout DescriptorSetLayout;

        public DescriptorSetLayoutWrapper(DescriptorSetLayout descriptorSetLayout)
        {
            DescriptorSetLayout = descriptorSetLayout;
        }
    }
}
