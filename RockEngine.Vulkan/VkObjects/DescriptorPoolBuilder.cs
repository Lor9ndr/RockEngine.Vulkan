using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class DescriptorPoolBuilder
    {
        private readonly VulkanContext _context;
        private readonly List<DescriptorPoolSize> _poolSizes = new List<DescriptorPoolSize>();

        public DescriptorPoolBuilder(VulkanContext context)
        {
            _context = context;
        }

        public DescriptorPoolBuilder AddPoolSize(DescriptorType type, uint count)
        {
            var poolSize = new DescriptorPoolSize
            {
                Type = type,
                DescriptorCount = count
            };
            _poolSizes.Add(poolSize);
            return this;
        }

        public unsafe DescriptorPoolWrapper Build(uint maxSets)
        {
            var poolSizes = _poolSizes.ToArray();
            fixed(DescriptorPoolSize* size = poolSizes)
            {
                var poolInfo = new DescriptorPoolCreateInfo
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Count,
                    PPoolSizes = size,
                    MaxSets = maxSets
                };
                return DescriptorPoolWrapper.Create(_context, in poolInfo, maxSets);
            }
        }
    }
}
