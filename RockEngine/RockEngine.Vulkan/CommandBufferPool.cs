using Silk.NET.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Vulkan
{
    public sealed class CommandBufferPool : IDisposable
    {
        private readonly VkCommandPool _commandPool;
        private readonly ConcurrentBag<VkCommandBuffer> _buffers = new();
        private bool _disposed;

        public CommandBufferPool(
            VulkanContext context,
            CommandPoolCreateFlags flags,
            uint queueFamilyIndex)
        {
            _commandPool = VkCommandPool.Create(context, flags, queueFamilyIndex);
        }
        public CommandBufferPool(VkCommandPool commandPool)
        {
            _commandPool = commandPool;
        }

        public VkCommandBuffer Get(CommandBufferLevel level)
        {
            if (_buffers.TryTake(out var buffer))
            {
                return buffer;
            }

            // Allocate new buffer if pool exhausted
            return _commandPool.AllocateCommandBuffer(level);
        }

        public void Return(VkCommandBuffer buffer)
        {
            if (!buffer.IsDisposed)
            {
                _buffers.Add(buffer);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var buffer in _buffers)
            {
                buffer.Dispose();
            }
            _buffers.Clear();
            _commandPool.Dispose();
        }
    }
}
