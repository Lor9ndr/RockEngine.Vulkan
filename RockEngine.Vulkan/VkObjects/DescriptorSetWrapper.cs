using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public struct DescriptorSetWrapper
    {
        public DescriptorSet DescriptorSet;
        public uint SetIndex;

        public DescriptorSetWrapper(DescriptorSet descriptorSet, uint setIndex)
        {
            DescriptorSet = descriptorSet;
            SetIndex = setIndex;
        }

        public static implicit operator DescriptorSet(DescriptorSetWrapper descriptorSet) => descriptorSet.DescriptorSet;
    }
}
