using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public struct DescriptorSetLayoutWrapper
    {
        public DescriptorSetLayout DescriptorSetLayout;
        public uint SetLocation;
        public readonly DescriptorSetLayoutBinding[] Bindings;

        public DescriptorSetLayoutWrapper(DescriptorSetLayout descriptorSetLayout, uint setLocation, DescriptorSetLayoutBinding[] bindignsArr)
        {
            DescriptorSetLayout = descriptorSetLayout;
            SetLocation = setLocation;
            Bindings = bindignsArr;
        }

    }
}
