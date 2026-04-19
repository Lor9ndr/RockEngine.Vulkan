using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class DescriptorPoolManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly DescriptorPoolSize[] _poolSizes;
        private readonly uint _maxSetsPerPool;
        private readonly DescriptorPoolCreateFlags _createFlags;
        private readonly ThreadLocal<List<VkDescriptorPool>> _pools;

        public DescriptorPoolManager(VulkanContext context, DescriptorPoolSize[] poolSizes,
            uint maxSetsPerPool, DescriptorPoolCreateFlags createFlags = DescriptorPoolCreateFlags.FreeDescriptorSetBit)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(poolSizes);

            _context = context;
            _poolSizes = poolSizes;
            _maxSetsPerPool = maxSetsPerPool;
            _createFlags = createFlags;
            _pools = new ThreadLocal<List<VkDescriptorPool>>(() =>
            {
                var list = new List<VkDescriptorPool>();
                CreateNewPool(list);
                return list;
            }, trackAllValues: true);
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
                    Flags = _createFlags
                };
                var pool = VkDescriptorPool.Create(_context, createInfo);
                targetList.Add(pool);
                return pool;
            }
        }

        /// <summary>
        /// Allocates a descriptor set for the given layout, optionally providing variable descriptor counts.
        /// </summary>
        /// <param name="layout">The descriptor set layout.</param>
        /// <param name="variableDescriptorCounts">
        /// If the layout contains variable‑sized bindings, this array must provide the actual descriptor count
        /// for each such binding, in the order of <see cref="VkDescriptorSetLayout.VariableBindingIndices"/>.
        /// </param>
        public unsafe VkDescriptorSet AllocateDescriptorSet(VkDescriptorSetLayout layout, uint[]? variableDescriptorCounts = null)
        {
            var currentPools = _pools.Value!;

            // Build the allocation info (same for all pools)
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorSetCount = 1,
                PSetLayouts = &layout.DescriptorSetLayout
            };

            // Chain variable count info if needed, keeping the fixed block alive
            DescriptorSetVariableDescriptorCountAllocateInfo variableCountInfo = default;
            if (variableDescriptorCounts != null && variableDescriptorCounts.Length > 0)
            {
                variableCountInfo.SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo;
                variableCountInfo.DescriptorSetCount = 1;
                fixed (uint* pCounts = variableDescriptorCounts)
                {
                    variableCountInfo.PDescriptorCounts = pCounts;
                    allocInfo.PNext = &variableCountInfo;

                    // Try each existing pool
                    foreach (var pool in currentPools)
                    {
                        allocInfo.DescriptorPool = pool;
                        var result = pool.AllocateDescriptorSet(layout, in allocInfo, out var set);
                        if (result == Result.Success)
                        {
                            return set;
                        }

                        if (result != Result.ErrorOutOfPoolMemory)
                        {
                            result.VkAssertResult("Failed to allocate descriptor set");
                        }
                    }
                }
            }
            else
            {
                // No variable counts – try each pool
                foreach (var pool in currentPools)
                {
                    allocInfo.DescriptorPool = pool;
                    var result = pool.AllocateDescriptorSet(layout, in allocInfo, out var set);
                    if (result == Result.Success)
                    {
                        return set;
                    }

                    if (result != Result.ErrorOutOfPoolMemory)
                    {
                        result.VkAssertResult("Failed to allocate descriptor set");
                    }
                }
            }

            // No pool succeeded – create a new one and try once
            var newPool = CreateNewPool(currentPools);
            allocInfo.DescriptorPool = newPool;
            // If variable counts were used, we must re-enter the fixed block, so we repeat the allocation logic.
            if (variableDescriptorCounts != null && variableDescriptorCounts.Length > 0)
            {
                fixed (uint* pCounts = variableDescriptorCounts)
                {
                    variableCountInfo.PDescriptorCounts = pCounts;
                    allocInfo.PNext = &variableCountInfo;
                    newPool.AllocateDescriptorSet(layout, in allocInfo, out var set).VkAssertResult("Failed to allocate descriptor set from new pool");
                    return set;
                }
            }
            else
            {
                newPool.AllocateDescriptorSet(layout, in allocInfo, out var set).VkAssertResult("Failed to allocate descriptor set from new pool");
                return set;
            }
        }

        public void Dispose()
        {
            foreach (var pools in _pools.Values)
            {
                foreach (var pool in pools)
                {
                    pool.Dispose();
                }
                pools.Clear();
            }
            _pools.Dispose();
        }
    }
}