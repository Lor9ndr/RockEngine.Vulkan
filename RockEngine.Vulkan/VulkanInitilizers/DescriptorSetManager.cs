using RockEngine.Vulkan.Helpers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VulkanInitilizers
{
    internal class DescriptorSetManager
    {
        private readonly VulkanContext _context;
        private readonly Dictionary<ulong, DescriptorSet> _descriptorSets;

        public DescriptorSetManager(VulkanContext context)
        {
            _context = context;
            _descriptorSets = new Dictionary<ulong, DescriptorSet>();
        }

        public DescriptorSet GetOrCreateDescriptorSet(in DescriptorSetLayout layout)
        {
            if (_descriptorSets.TryGetValue(layout.Handle, out var set))
            {
                return set;
            }

            var newSet = AllocateDescriptorSet(layout);
            _descriptorSets[layout.Handle] = newSet;
            return newSet;
        }

        public void UpdateDescriptorSet(WriteDescriptorSet[] writes)
        {
            _context.Api.UpdateDescriptorSets(_context.Device, (uint)writes.Length, writes.AsSpan(), 0, ReadOnlySpan<CopyDescriptorSet>.Empty);
        }

        private unsafe DescriptorSet AllocateDescriptorSet(in DescriptorSetLayout layout)
        {
            var pSetLayouts = stackalloc DescriptorSetLayout[1] { layout };
            var allocInfo = new DescriptorSetAllocateInfo()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _context.DescriptorPoolFactory.GetOrCreatePool(),
                DescriptorSetCount = 1,
                PSetLayouts = pSetLayouts,
            };
            _context.Api.AllocateDescriptorSets(_context.Device, in allocInfo,  out var descriptorSet)
                .ThrowCode("Failed to allocate descriptorSet");
            return descriptorSet;
        }
    }
}
