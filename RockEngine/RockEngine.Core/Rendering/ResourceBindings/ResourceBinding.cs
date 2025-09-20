using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public abstract class ResourceBinding(uint setLocation, uint bindingLocation)
    {
        public VkDescriptorSet[] DescriptorSets { get;set; }  = new VkDescriptorSet[VulkanContext.GetCurrent().MaxFramesPerFlight];
        //public VkDescriptorSet? DescriptorSet { get; set; }
        public uint SetLocation { get; set; } = setLocation;
        public uint BindingLocation { get; } = bindingLocation;
        protected abstract DescriptorType DescriptorType { get; }

        public abstract void UpdateDescriptorSet(VulkanContext renderingContext, uint frameIndex);
    }
}
