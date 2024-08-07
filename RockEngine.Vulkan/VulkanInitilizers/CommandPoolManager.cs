﻿using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Vulkan.VulkanInitilizers
{
    public class CommandPoolManager : IDisposable
    {
        private readonly VulkanContext _context;
        //private readonly ConcurrentDictionary<int, CommandPoolWrapper> _commandPools;

        public CommandPoolManager(VulkanContext context)
        {
            _context = context;
            //_commandPools = new ConcurrentDictionary<int, CommandPoolWrapper>();
        }

        public CommandPoolWrapper GetCommandPool()
        {
            int threadId = Environment.CurrentManagedThreadId;

           //if (!_commandPools.TryGetValue(threadId, out CommandPoolWrapper commandPool))
           //{
           //    _commandPools[threadId] = commandPool;
           //}
           var commandPool = CreateCommandPool();

            return commandPool;
        }

        private CommandPoolWrapper CreateCommandPool()
        {
            CommandPoolCreateInfo ci = new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
                QueueFamilyIndex = _context.Device.QueueFamilyIndices.GraphicsFamily.Value
            };
            return CommandPoolWrapper.Create(_context, in ci);
        }

        public void Dispose()
        {
/*            foreach (var commandPool in _commandPools.Values)
            {
                commandPool.Dispose();
            }
            _commandPools.Clear();*/
        }
    }
}
