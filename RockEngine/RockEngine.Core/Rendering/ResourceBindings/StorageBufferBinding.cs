using RockEngine.Core.Rendering.Buffers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class StorageBufferBinding<T> : ResourceBinding where T:unmanaged
    {
        public StorageBuffer<T> Buffer { get; }
        public ulong Offset { get; }

        public override DescriptorType DescriptorType => DescriptorType.StorageBuffer;

        public StorageBufferBinding(StorageBuffer<T> buffer, uint bindingLocation, uint setLocation, ulong offset = 0)
            : base(setLocation, new Internal.UIntRange(bindingLocation, bindingLocation))
        {
            Buffer = buffer;
            Offset = offset;
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext renderingContext, uint frameIndex)
        {
            var descriptor = DescriptorSets[frameIndex];
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = Buffer.Buffer,
                Offset = 0,
                Range = Buffer.Buffer.Size
            };

            var writeDescriptorSet = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptor,
                DstBinding = BindingLocation.Start,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };

            VulkanContext.Vk.UpdateDescriptorSets(renderingContext.Device, 1, in writeDescriptorSet, 0, null);
        }
        public override StorageBufferBinding<T> Clone()
        {
            return new StorageBufferBinding<T>(Buffer, BindingLocation.Start, SetLocation, Offset);
        }
    }
}
