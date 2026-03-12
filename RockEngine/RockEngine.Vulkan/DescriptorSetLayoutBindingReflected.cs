using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public unsafe record DescriptorSetLayoutBindingReflected
    {
        public string? Name { get; set; }
        public uint Binding { get; set; }
        public DescriptorType DescriptorType { get; }
        public uint DescriptorCount { get; }
        public ShaderStageFlags StageFlags { get; set; }
        public nint PImmutableSamplers { get; internal set; }


        public DescriptorSetLayoutBindingReflected(string? name, uint binding, DescriptorType descriptorType, uint descriptorCount, ShaderStageFlags stageFlags, nint pImmutableSamplers )
        {
            Name = name;
            Binding = binding;
            DescriptorType = descriptorType;
            DescriptorCount = descriptorCount;
            StageFlags = stageFlags;
            PImmutableSamplers = pImmutableSamplers;
        }


             public DescriptorSetLayoutBindingReflected(string? name, uint binding, DescriptorType descriptorType, uint descriptorCount, ShaderStageFlags stageFlags)
        {
            Name = name;
            Binding = binding;
            DescriptorType = descriptorType;
            DescriptorCount = descriptorCount;
            StageFlags = stageFlags;
            PImmutableSamplers = default;
        }

        public DescriptorSetLayoutBindingReflected(string name, DescriptorSetLayoutBinding setLayoutBinding)
            : this(name,
                 setLayoutBinding.Binding,
                 setLayoutBinding.DescriptorType,
                 setLayoutBinding.DescriptorCount,
                 setLayoutBinding.StageFlags,
                 (nint)setLayoutBinding.PImmutableSamplers)
        { }

        public static implicit operator DescriptorSetLayoutBinding(DescriptorSetLayoutBindingReflected value)
            => new DescriptorSetLayoutBinding(value.Binding, value.DescriptorType, value.DescriptorCount, value.StageFlags, (Sampler*)value.PImmutableSamplers);


    }
}