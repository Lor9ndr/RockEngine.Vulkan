using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class DescriptorPoolManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly DescriptorPoolSize[] _poolSizes;
        private readonly uint _maxSetsPerPool;
        private readonly ThreadLocal<List<VkDescriptorPool>> _pools;
        private readonly ThreadLocal<List<VkDescriptorPool>> _exhaustedPools;

        public DescriptorPoolManager(VulkanContext context, DescriptorPoolSize[] poolSizes, uint maxSetsPerPool)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));
            ArgumentNullException.ThrowIfNull(poolSizes, nameof(poolSizes));
            _context = context;
            _poolSizes = poolSizes;
            _maxSetsPerPool = maxSetsPerPool;

            _pools = new ThreadLocal<List<VkDescriptorPool>>(() =>
            {
                var list = new List<VkDescriptorPool>();
                CreateNewPool(list); // Create initial pool for each thread
                return list;
            });

            _exhaustedPools = new ThreadLocal<List<VkDescriptorPool>>(() => new List<VkDescriptorPool>());
        }

        private unsafe VkDescriptorPool CreateNewPool(List<VkDescriptorPool> targetList)
        {
            fixed (DescriptorPoolSize* poolSizesPtr = _poolSizes)
            {
                var createInfo = new DescriptorPoolCreateInfo
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)_poolSizes.Length,
                    PPoolSizes = poolSizesPtr,
                    MaxSets = _maxSetsPerPool,
                    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                };

                var pool = VkDescriptorPool.Create(_context, createInfo);
                targetList.Add(pool);
                return pool;
            }
        }

        public unsafe VkDescriptorSet AllocateDescriptorSet(VkDescriptorSetLayout layout)
        {
            var currentPools = _pools.Value!;
            var currentExhausted = _exhaustedPools.Value!;

            foreach (var pool in currentPools)
            {
                if (currentExhausted.Contains(pool))
                    continue;

                var result = pool.AllocateDescriptorSet(layout, out var descriptorSet)
                    .VkAssertResult("Failed to allocate descriptor set", Result.ErrorOutOfPoolMemory);

                if (result == Result.Success)
                    return descriptorSet;

                currentExhausted.Add(pool);
            }

            // All pools exhausted, create new one
            var newPool = CreateNewPool(currentPools);
            newPool.AllocateDescriptorSet(layout, out var newDescriptorSet)
                .VkAssertResult("Failed to allocate descriptor set from new pool");

            return newDescriptorSet;
        }

        public void Dispose()
        {
            foreach (var pools in _pools.Values)
            {
                foreach (var pool in pools)
                    pool.Dispose();
                pools.Clear();
            }
            _pools.Dispose();

            foreach (var exhausted in _exhaustedPools.Values)
                exhausted.Clear();
            _exhaustedPools.Dispose();
        }
    }
}