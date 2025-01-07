using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Commands
{
    internal readonly record struct DescriptorBindingCommand : IRenderCommand
    {
        public DescriptorSet DescriptorSet { get; }
        public uint SetLocation { get; }
        public uint BindingLocation { get; }

        public DescriptorBindingCommand(DescriptorSet descriptorSet, uint setLocation, uint bindingLocation)
        {
            DescriptorSet = descriptorSet;
            SetLocation = setLocation;
            BindingLocation = bindingLocation;
        }
    }
}
