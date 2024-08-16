using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Diagnostics;

namespace RockEngine.Vulkan.VkObjects
{
    public class CommandPoolWrapper : VkObject<CommandPool>
    {
        private readonly VulkanContext _context;
        private List<CommandBufferWrapper> _commandBuffers = new List<CommandBufferWrapper>();

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

        public CommandBufferWrapper AllocateCommandBuffer(CommandBufferLevel level = CommandBufferLevel.Primary)
        {
            CommandBufferAllocateInfo allocateInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _vkObject,
                Level = level,
                CommandBufferCount = 1
            };
            
            return AllocateCommandBuffer(in allocateInfo);
        }
        public CommandBufferWrapper AllocateCommandBuffer(in CommandBufferAllocateInfo allocInfo)
        {

            _context.Api.AllocateCommandBuffers(_context.Device, in allocInfo, out CommandBuffer cbNative)
              .ThrowCode("Failed to allocate command buffer");
            //Debugger.Log(1, "Allocation", $"Allocated a command buffer with handle: {cbNative.Handle}");

            var cb = new CommandBufferWrapper(_context, in cbNative, this);
            _commandBuffers.Add(cb);
            return cb;
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

        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                unsafe
                {
                    fixed (CommandBuffer* pCb = _commandBuffers.Select(s=>s.VkObjectNative).ToArray())
                    {
                        _context.Api.FreeCommandBuffers(_context.Device, this, (uint)_commandBuffers.Count, pCb);
                    }
                    foreach (var item in _commandBuffers)
                    {
                        item.Dispose();
                    }
                    _context.Api.DestroyCommandPool(_context.Device, _vkObject, null);
                }

                _disposed = true;
            }
        }
    }
}