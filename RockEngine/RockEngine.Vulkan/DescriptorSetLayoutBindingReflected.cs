using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public unsafe class DescriptorSetLayoutBindingReflected
    {
        public string? Name { get; set; }
        public uint Binding { get; set; }
        public DescriptorType DescriptorType { get; }
        public uint DescriptorCount { get; }
        public ShaderStageFlags StageFlags { get; }
        public unsafe Sampler* PImmutableSamplers { get; internal set; }


        public DescriptorSetLayoutBindingReflected(string? name, uint binding, DescriptorType descriptorType, uint descriptorCount, ShaderStageFlags stageFlags, Silk.NET.Vulkan.Sampler* pImmutableSamplers)
        {
            Name = name;
            Binding = binding;
            DescriptorType = descriptorType;
            DescriptorCount = descriptorCount;
            StageFlags = stageFlags;
            PImmutableSamplers = pImmutableSamplers;
        }

        public DescriptorSetLayoutBindingReflected(string name, DescriptorSetLayoutBinding setLayoutBinding)
            : this(name,
                 setLayoutBinding.Binding,
                 setLayoutBinding.DescriptorType,
                 setLayoutBinding.DescriptorCount,
                 setLayoutBinding.StageFlags,
                 setLayoutBinding.PImmutableSamplers)
        { }

        public static implicit operator DescriptorSetLayoutBinding(DescriptorSetLayoutBindingReflected value) 
            => new DescriptorSetLayoutBinding(value.Binding, value.DescriptorType, value.DescriptorCount, value.StageFlags, value.PImmutableSamplers);


    }
}