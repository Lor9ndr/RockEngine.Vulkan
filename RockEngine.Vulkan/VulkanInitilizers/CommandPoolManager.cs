using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Vulkan.VulkanInitilizers
{

    public class CommandPoolManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly ConcurrentDictionary<int, VulkanCommandPool> _commandPools;

        public CommandPoolManager(VulkanContext context)
        {
            _context = context;
            _commandPools = new ConcurrentDictionary<int, VulkanCommandPool>();
        }

        public VulkanCommandPool GetCommandPool()
        {
            int threadId = Environment.CurrentManagedThreadId;

            if (!_commandPools.TryGetValue(threadId, out VulkanCommandPool commandPool))
            {
                commandPool = CreateCommandPool();
                _commandPools[threadId] = commandPool;
            }

            return commandPool;
        }

        private VulkanCommandPool CreateCommandPool()
        {
            using var cpBuilder = new VulkanCommandPoolBuilder(_context.Api, _context.Device);
            var commandPool = cpBuilder
                .WithFlags(CommandPoolCreateFlags.ResetCommandBufferBit)
                .WithQueueFamilyIndex(_context.Device.QueueFamilyIndices.GraphicsFamily.Value)
                .Build();

            return commandPool;
        }

        public void Dispose()
        {
            foreach (var commandPool in _commandPools.Values)
            {
                commandPool.Dispose();
            }
            _commandPools.Clear();
        }
    }
}
