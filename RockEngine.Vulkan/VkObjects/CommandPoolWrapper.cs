using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class CommandPoolWrapper : VkObject<CommandPool>
    {
        private readonly VulkanContext _context;

        public CommandPoolWrapper(VulkanContext context, CommandPool commandPool)
            :base(commandPool)
        {
            _context = context;
        }


        public static unsafe CommandPoolWrapper Create(VulkanContext context, in CommandPoolCreateInfo ci)
        {
            context.Api.CreateCommandPool(context.Device, in ci, default, out var commandPool);
            return new CommandPoolWrapper(context, commandPool);
        }

        public CommandBuffer AllocateCommandBuffer(CommandBufferLevel level = CommandBufferLevel.Primary)
        {
            CommandBufferAllocateInfo allocateInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _vkObject,
                Level = level,
                CommandBufferCount = 1
            };

            _context.Api.AllocateCommandBuffers(_context.Device, ref allocateInfo, out CommandBuffer commandBuffer);
            return commandBuffer;
        }

        public CommandBuffer[] AllocateCommandBuffers(uint count, CommandBufferLevel level = CommandBufferLevel.Primary)
        {
            CommandBufferAllocateInfo allocateInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _vkObject,
                Level = level,
                CommandBufferCount = count
            };

            CommandBuffer[] commandBuffers = new CommandBuffer[count];
            unsafe
            {
                fixed (CommandBuffer* pCommandBuffers = commandBuffers)
                {
                    _context.Api.AllocateCommandBuffers(_context.Device, ref allocateInfo, pCommandBuffers);
                }
            }
            return commandBuffers;
        }

        public void FreeCommandBuffer(CommandBuffer commandBuffer)
        {
            unsafe
            {
                _context.Api.FreeCommandBuffers(_context.Device, _vkObject, 1, &commandBuffer);
            }
        }

        public void FreeCommandBuffers(CommandBuffer[] commandBuffers)
        {
            unsafe
            {
                fixed (CommandBuffer* pCommandBuffers = commandBuffers)
                {
                    _context.Api.FreeCommandBuffers(_context.Device, _vkObject, (uint)commandBuffers.Length, pCommandBuffers);
                }
            }
        }

        public void ResetCommandPool(CommandPoolResetFlags flags = 0)
        {
            _context.Api.ResetCommandPool(_context.Device, _vkObject, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                unsafe
                {
                    _context.Api.DestroyCommandPool(_context.Device, _vkObject, null);
                }

                _disposed = true;
            }
        }
    }
}