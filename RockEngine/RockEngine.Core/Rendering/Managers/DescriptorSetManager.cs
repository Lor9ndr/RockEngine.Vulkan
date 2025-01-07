using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Core.Rendering.Managers
{
    public class DescriptorSetManager
    {
        private readonly RenderingContext _context;
        private readonly VkDescriptorPool _descriptorPool;

        public DescriptorSetManager(RenderingContext context, VkDescriptorPool descriptorPool)
        {
            _context = context;
            _descriptorPool = descriptorPool;
        }

        public unsafe DescriptorSet AllocateDescriptorSet(VkDescriptorSetLayout layout)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout.DescriptorSetLayout
            };

            RenderingContext.Vk.AllocateDescriptorSets(_context.Device, &allocInfo, out var newSet)
                .VkAssertResult("Failed to allocate descriptor sets");

            return newSet;
        }



        public unsafe void UpdateDescriptorSet(DescriptorSet descriptorSet, UniformBuffer descriptorResource)
        {
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = descriptorResource.Buffer,
                Offset = 0,
                Range = descriptorResource.Size
            };

            var set = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = descriptorResource.BindingLocation,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };

            RenderingContext.Vk.UpdateDescriptorSets(_context.Device, 1, in set, 0, null);
        }
    }
}
