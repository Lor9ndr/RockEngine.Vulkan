using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Vulkan.VkObjects
{
    public class DescriptorPoolFactory : IDisposable
    {
        private readonly ConcurrentBag<DescriptorPoolWrapper> _pools = new ConcurrentBag<DescriptorPoolWrapper>();
        private readonly VulkanContext _context;
        private bool _disposed;
        private int _currentPoolIndex = 0;

        public unsafe DescriptorPoolFactory(VulkanContext context, uint poolCount = 3, uint maxSetsPerPool = 500)
        {
            _context = context;

            // Create default pool sizes
            var poolSizes = new[]
            {
                new DescriptorPoolSize(DescriptorType.UniformBuffer, 500),
                new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 500),
                new DescriptorPoolSize(DescriptorType.StorageBuffer, 500)
            };

            // Create multiple descriptor pools
            for (int i = 0; i < poolCount; i++)
            {
                fixed (DescriptorPoolSize* pPoolSizes = poolSizes)
                {
                    var poolInfo = new DescriptorPoolCreateInfo
                    {
                        SType = StructureType.DescriptorPoolCreateInfo,
                        PoolSizeCount = (uint)poolSizes.Length,
                        PPoolSizes = pPoolSizes,
                        MaxSets = maxSetsPerPool,
                        Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
                    };
                    var descriptorPool = DescriptorPoolWrapper.Create(_context, in poolInfo, maxSetsPerPool);
                    _pools.Add(descriptorPool);
                }
            }
        }

        public DescriptorPoolWrapper GetOrCreatePool()
        {
            if (_pools.IsEmpty)
            {
                throw new InvalidOperationException("No descriptor pools available.");
            }

            // Simple round-robin selection of pools
            var poolIndex = Interlocked.Increment(ref _currentPoolIndex) % _pools.Count;
            return _pools.ElementAt(poolIndex);
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
