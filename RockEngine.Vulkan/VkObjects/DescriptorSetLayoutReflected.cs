using RockEngine.Vulkan.VkObjects.Reflected;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    internal class DescriptorSetLayoutReflected
    {
        internal uint Set;

        public List<DescriptorSetLayoutBindingReflected> Bindings { get; set; }
    }
}