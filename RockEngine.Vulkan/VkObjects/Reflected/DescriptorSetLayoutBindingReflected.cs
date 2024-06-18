using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects.Reflected
{
    internal class DescriptorSetLayoutBindingReflected
    {
        public string? Name { get;set;}

        public uint Binding { get; set; }
        public DescriptorType DescriptorType { get; set; }
        public uint DescriptorCount { get; set; }
        public ShaderStageFlags StageFlags { get; set; }
        public unsafe Silk.NET.Vulkan.Sampler* PImmutableSamplers { get; internal set; }
    }
}