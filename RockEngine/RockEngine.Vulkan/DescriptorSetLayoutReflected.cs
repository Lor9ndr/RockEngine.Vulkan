using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    internal class DescriptorSetLayoutReflected
    {
        internal uint Set;

        public List<DescriptorSetLayoutBindingReflected> Bindings { get; set; }
    }
}