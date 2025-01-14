using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class UniformBufferBinding : ResourceBinding
    {
        public UniformBuffer Buffer { get; }
        public ulong Offset { get; }
        public UniformBufferBinding(UniformBuffer buffer, uint bindingLocation, uint setLocation, ulong offset = 0)
            : base(setLocation, bindingLocation)
        {
            Buffer = buffer;
            Offset = offset;
        }

        public unsafe override void UpdateDescriptorSet(RenderingContext renderingContext)
        {
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = Buffer.Buffer,
                Offset = 0,
                Range = (ulong)Buffer.DataSize,
            };

            var writeDescriptorSet = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = DescriptorSet,
                DstBinding = BindingLocation,
                DstArrayElement = 0,
                DescriptorType = Buffer.IsDynamic ? DescriptorType.UniformBufferDynamic : DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };

            RenderingContext.Vk.UpdateDescriptorSets(renderingContext.Device, 1, in writeDescriptorSet, 0, null);
            IsDirty = false;
        }
    }
}
