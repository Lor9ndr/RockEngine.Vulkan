using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public struct DescriptorSetWrapper
    {
        public DescriptorSet DescriptorSet;
        public uint SetIndex;
        public bool Updated;

        public DescriptorSetWrapper(DescriptorSet descriptorSet, uint setIndex, bool updated = false)
        {
            DescriptorSet = descriptorSet;
            SetIndex = setIndex;
            Updated = updated;
        }

        public static implicit operator DescriptorSet(DescriptorSetWrapper descriptorSet) => descriptorSet.DescriptorSet;
    }
}
