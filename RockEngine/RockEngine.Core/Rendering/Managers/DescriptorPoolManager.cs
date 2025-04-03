using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class DescriptorPoolManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly DescriptorPoolSize[] _poolSizes;
        private readonly uint _maxSetsPerPool;
        private readonly List<VkDescriptorPool> _pools = new List<VkDescriptorPool>();
        private readonly List<VkDescriptorPool> _exhaustedPools = new List<VkDescriptorPool>();

        public DescriptorPoolManager(VulkanContext context, DescriptorPoolSize[] poolSizes, uint maxSetsPerPool)
        {
            _context = context;
            _poolSizes = poolSizes ?? throw new ArgumentNullException(nameof(poolSizes));
            _maxSetsPerPool = maxSetsPerPool;
            CreateNewPool();
        }

        private unsafe VkDescriptorPool CreateNewPool()
        {
            fixed (DescriptorPoolSize* poolSizesPtr = _poolSizes)
            {
                var createInfo = new DescriptorPoolCreateInfo
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = poolSizesPtr,
                    MaxSets = _maxSetsPerPool,
                    Flags = DescriptorPoolCreateFlags.None
                };

                var pool = VkDescriptorPool.Create(_context, createInfo);
                _pools.Add(pool);
                return pool;
            }
        }

        public unsafe DescriptorSet AllocateDescriptorSet(VkDescriptorSetLayout layout)
        {
            foreach (var pool in _pools)
            {
                if (_exhaustedPools.Contains(pool))
                {
                    continue;
                }

                var allocInfo = new DescriptorSetAllocateInfo
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = pool,
                    DescriptorSetCount = 1,
                    PSetLayouts = &layout.DescriptorSetLayout
                };

                var result = VulkanContext.Vk.AllocateDescriptorSets(_context.Device, &allocInfo, out DescriptorSet descriptorSet)
                    .VkAssertResult("Failed to allocate descriptor set", Result.ErrorOutOfPoolMemory);
                if (result == Result.Success)
                {
                    return descriptorSet;
                }
                _exhaustedPools.Add(pool);


            }

            // All existing pools exhausted; create a new one
            var newPool = CreateNewPool();
            var newAllocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = newPool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout.DescriptorSetLayout
            };

            DescriptorSet newDescriptorSet;
            VulkanContext.Vk.AllocateDescriptorSets(_context.Device, &newAllocInfo, out newDescriptorSet)
                .VkAssertResult("Failed to allocate descriptor set from new pool");
            return newDescriptorSet;
        }

        public void Dispose()
        {
            foreach (var pool in _pools)
            {
                pool.Dispose();
            }
            _pools.Clear();
            _exhaustedPools.Clear();
        }
    }
}