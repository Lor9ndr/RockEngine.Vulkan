using RockEngine.Vulkan.VkObjects.Reflected;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public struct DescriptorSetLayoutWrapper
    {
        public DescriptorSetLayout DescriptorSetLayout;
        public uint SetLocation;
        public readonly DescriptorSetLayoutBindingReflected[] Bindings;

        public DescriptorSetLayoutWrapper(DescriptorSetLayout descriptorSetLayout, uint setLocation, DescriptorSetLayoutBindingReflected[] bindignsArr)
        {
            DescriptorSetLayout = descriptorSetLayout;
            SetLocation = setLocation;
            Bindings = bindignsArr;
        }
    }
}
