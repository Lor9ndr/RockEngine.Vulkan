using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class DescriptorPoolFactory : IDisposable
    {
        private readonly List<DescriptorPoolWrapper> _pools = new List<DescriptorPoolWrapper>();
        private readonly Dictionary<DescriptorPoolWrapper, List<DescriptorPoolSize>> _poolSizeMap = new Dictionary<DescriptorPoolWrapper, List<DescriptorPoolSize>>();
        private readonly Dictionary<DescriptorPoolWrapper, uint> _poolUsageMap = new Dictionary<DescriptorPoolWrapper, uint>();
        private readonly VulkanContext _context;
        private bool _disposed;

        public DescriptorPoolFactory(VulkanContext context)
        {
            _context = context;
        }

        public unsafe DescriptorPoolWrapper GetOrCreatePool(uint maxSets, DescriptorPoolSize[] poolSizes, DescriptorPoolCreateFlags flags = DescriptorPoolCreateFlags.None)
        {
            // Check if there is an existing pool with enough available space and matching pool sizes
            foreach (var pool in _pools)
            {
                if (_poolSizeMap[pool] == null)
                {
                    continue;
                }
                if (_poolUsageMap[pool] + maxSets <= pool.MaxSets && PoolSizesMatch(_poolSizeMap[pool], poolSizes))
                {
                    _poolUsageMap[pool] += maxSets;
                    return pool;
                }
            }

            // If no existing pool has enough space, create a new pool
            fixed (DescriptorPoolSize* pPoolSizes = poolSizes)
            {
                var poolInfo = new DescriptorPoolCreateInfo
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    PoolSizeCount = (uint)poolSizes.Length,
                    PPoolSizes = pPoolSizes,
                    MaxSets = maxSets,
                    Flags = flags
                };
                var descriptorPool = DescriptorPoolWrapper.Create(_context, in poolInfo, maxSets);

                _pools.Add(descriptorPool);
                _poolSizeMap[descriptorPool] = new List<DescriptorPoolSize>(poolSizes);
                _poolUsageMap[descriptorPool] = maxSets;
                return descriptorPool;
            }
        }

        private bool PoolSizesMatch(List<DescriptorPoolSize> existingPoolSizes, DescriptorPoolSize[] newPoolSizes)
        {
            if (existingPoolSizes.Count != newPoolSizes.Length)
            {
                return false;
            }

            for (int i = 0; i < existingPoolSizes.Count; i++)
            {
                if (existingPoolSizes[i].Type != newPoolSizes[i].Type || existingPoolSizes[i].DescriptorCount != newPoolSizes[i].DescriptorCount)
                {
                    return false;
                }
            }

            return true;
        }

        public unsafe void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            foreach (var pool in _pools)
            {
                _context.Api.DestroyDescriptorPool(_context.Device, pool, null);
            }
            _disposed = true;
        }
    }
}