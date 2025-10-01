using RockEngine.Core.Internal;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public abstract class ResourceBinding(uint setLocation, UIntRange bindingLocation) :ICloneable
    {
        public VkDescriptorSet[] DescriptorSets { get;set; }  = new VkDescriptorSet[VulkanContext.GetCurrent().MaxFramesPerFlight];
        //public VkDescriptorSet? DescriptorSet { get; set; }
        public uint SetLocation { get; set; } = setLocation;
        public UIntRange BindingLocation { get; } = bindingLocation;
        public abstract DescriptorType DescriptorType { get; }

        public abstract object Clone();

        public abstract void UpdateDescriptorSet(VulkanContext renderingContext, uint frameIndex);

    }
}
