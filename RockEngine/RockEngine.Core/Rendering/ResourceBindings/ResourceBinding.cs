using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public abstract class ResourceBinding(uint setLocation, uint bindingLocation)
    {
        public VkDescriptorSet? DescriptorSet { get; set; }
        public uint SetLocation { get; set; } = setLocation;
        public uint BindingLocation { get; } = bindingLocation;
        public bool IsDirty { get; set; } = true;
        protected abstract DescriptorType DescriptorType { get; }

        public abstract void UpdateDescriptorSet(VulkanContext renderingContext);

        public virtual int GetResourceHash()
        {
            HashCode hash = new HashCode();
            hash.Add(SetLocation);
            hash.Add(BindingLocation);
            hash.Add(DescriptorType);
            return hash.ToHashCode();
        }

    }
}
