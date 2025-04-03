using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class UniformBufferBinding : ResourceBinding
    {
        private readonly bool _updateWholeData;

        public UniformBuffer Buffer { get; }
        public ulong Offset { get; }
        public UniformBufferBinding(UniformBuffer buffer, uint bindingLocation, uint setLocation, ulong offset = 0, bool updateWholeData = false)
            : base(setLocation, bindingLocation)
        {
            Buffer = buffer;
            Offset = offset;
            _updateWholeData = updateWholeData;
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext renderingContext)
        {
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = Buffer.Buffer,
                Offset = 0,
                Range = _updateWholeData ? Buffer.Buffer.Size : (ulong)Buffer.DataSize,
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

            VulkanContext.Vk.UpdateDescriptorSets(renderingContext.Device, 1, in writeDescriptorSet, 0, null);
            IsDirty = false;
        }
    }
}
