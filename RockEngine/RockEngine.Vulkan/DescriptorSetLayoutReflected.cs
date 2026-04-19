namespace RockEngine.Vulkan
{
    internal class DescriptorSetLayoutReflected
    {
        public uint Set;

        public required DescriptorSetLayoutBindingReflected[] Bindings;
    }
}