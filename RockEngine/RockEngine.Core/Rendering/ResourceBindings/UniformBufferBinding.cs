using RockEngine.Core.Rendering.Buffers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class UniformBufferBinding : ResourceBinding
    {
        public UniformBuffer Buffer { get; }
        public ulong Offset { get; }

        private readonly ulong? _elementSize;

        public override DescriptorType DescriptorType => Buffer.IsDynamic ? DescriptorType.UniformBufferDynamic : DescriptorType.UniformBuffer;

        public UniformBufferBinding(
            UniformBuffer buffer,
            uint bindingLocation,
            uint setLocation,
            ulong offset = 0,
            ulong? elementSize = null)
            : base(setLocation, new Internal.UIntRange(bindingLocation, bindingLocation))
        {
            Buffer = buffer;
            Offset = offset;
            _elementSize = elementSize;
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext renderingContext, uint frameIndex)
        {
            var descriptor = DescriptorSets[frameIndex];
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = Buffer.Buffer,
                Offset = 0,
                Range = _elementSize ?? Buffer.Buffer.Size
            };

            var writeDescriptorSet = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptor,
                DstBinding = BindingLocation.Start,
                DstArrayElement = 0,
                DescriptorType = DescriptorType,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };

            VulkanContext.Vk.UpdateDescriptorSets(renderingContext.Device, 1, in writeDescriptorSet, 0, null);
        }

        public override UniformBufferBinding Clone()
        {
            return new UniformBufferBinding(Buffer, BindingLocation.Start, SetLocation, Offset, _elementSize);
        }
    }
}
