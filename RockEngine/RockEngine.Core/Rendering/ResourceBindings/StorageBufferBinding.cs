using RockEngine.Core.Rendering.Buffers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class StorageBufferBinding<T> : ResourceBinding where T:unmanaged
    {
        public StorageBuffer<T> Buffer { get; }
        public ulong Offset { get; }

        protected override DescriptorType DescriptorType => DescriptorType.StorageBuffer;

        public StorageBufferBinding(StorageBuffer<T> buffer, uint bindingLocation, uint setLocation, ulong offset = 0)
            : base(setLocation, bindingLocation)
        {
            Buffer = buffer;
            Offset = offset;
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext renderingContext)
        {
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = Buffer.Buffer,
                Offset = 0,
                Range = Buffer.Buffer.Size
            };

            var writeDescriptorSet = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = DescriptorSet,
                DstBinding = BindingLocation,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };

            VulkanContext.Vk.UpdateDescriptorSets(renderingContext.Device, 1, in writeDescriptorSet, 0, null);
            IsDirty = false;
        }
        public override int GetResourceHash()
        {
            HashCode hash = new HashCode();
            hash.Add(base.GetResourceHash());
            hash.Add(Buffer.GetHashCode());
            return hash.ToHashCode();
        }
    }
}
