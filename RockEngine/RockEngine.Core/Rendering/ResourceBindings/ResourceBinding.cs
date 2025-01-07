using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public abstract class ResourceBinding(uint setLocation)
    {
        public DescriptorSet DescriptorSet { get; set; }
        public uint SetLocation { get; set; } = setLocation;
    }

}
