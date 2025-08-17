using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class ThreadSafeCommandPool
    {
        private readonly ThreadLocal<VkCommandPool> _threadLocalPools;
        private readonly VulkanContext _context;

        public ThreadSafeCommandPool(VulkanContext context, CommandPoolCreateFlags flags, uint queueFamilyIndex)
        {
            _context = context;
            _threadLocalPools = new ThreadLocal<VkCommandPool>(() =>
                VkCommandPool.Create(_context,  new CommandPoolCreateInfo()
                {
                    SType = StructureType.CommandPoolCreateInfo,
                    QueueFamilyIndex  = queueFamilyIndex,
                    Flags = flags
                }));
        }

        public VkCommandBuffer[] Allocate(uint count, CommandBufferLevel level)
        {
            return _threadLocalPools.Value!.AllocateCommandBuffers(count, level);
        }

        public void Dispose()
        {
            foreach (var pool in _threadLocalPools.Values)
            {
                pool.Dispose();
            }
            _threadLocalPools.Dispose();
        }
    }
}
