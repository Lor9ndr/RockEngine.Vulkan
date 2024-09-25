using Silk.NET.Vulkan;


namespace RockEngine.Vulkan
{
    public record VkCommandPool : VkObject<CommandPool>
    {
        private readonly RenderingContext _context;
        private readonly List<VkCommandBuffer> _commandBuffers = new List<VkCommandBuffer>();

        public VkCommandPool(RenderingContext context, CommandPool commandPool)
            : base(commandPool)
        {
            _context = context;
        }

        public static unsafe VkCommandPool Create(RenderingContext context, in CommandPoolCreateInfo ci)
        {
            RenderingContext.Vk.CreateCommandPool(context.Device, in ci, in RenderingContext.CustomAllocator<VkCommandPool>(), out var commandPool);
            return new VkCommandPool(context, commandPool);
        }

        public static unsafe VkCommandPool Create(RenderingContext context, CommandPoolCreateFlags flags, uint queueFamilyIndex)
        {
            CommandPoolCreateInfo ci = new CommandPoolCreateInfo(StructureType.CommandPoolCreateInfo,
                                                                 flags: flags,
                                                                 queueFamilyIndex: queueFamilyIndex);
            
            return Create(context, in ci);
        }
        

        public VkCommandBuffer AllocateCommandBuffer(CommandBufferLevel level = CommandBufferLevel.Primary)
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
        public unsafe VkCommandBuffer AllocateCommandBuffer(in CommandBufferAllocateInfo allocInfo)
        {

            CommandBuffer cbNative = default;
            RenderingContext.Vk.AllocateCommandBuffers(_context.Device, in allocInfo, &cbNative)
              .VkAssertResult("Failed to allocate command buffer");
            //Debugger.Log(1, "Allocation", $"Allocated a command buffer with handle: {cbNative.Handle}");

            var cb = new VkCommandBuffer(_context, in cbNative, this);
            _commandBuffers.Add(cb);
            return cb;
        }

        public VkCommandBuffer[] AllocateCommandBuffers(uint count, CommandBufferLevel level = CommandBufferLevel.Primary)
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
                    RenderingContext.Vk.AllocateCommandBuffers(_context.Device, ref allocateInfo, pCommandBuffers);
                }
            }
            return commandBuffers.Select(s=>new VkCommandBuffer(_context, in s, this)).ToArray();
        }

        public unsafe void FreeCommandBuffer(VkCommandBuffer commandBuffer)
        {
            var buffer = commandBuffer.VkObjectNative;

            RenderingContext.Vk.FreeCommandBuffers(_context.Device, _vkObject, 1,&buffer);

            commandBuffer.Dispose();
            _commandBuffers.Remove(commandBuffer);
        }

        public void FreeCommandBuffers(VkCommandBuffer[] commandBuffers)
        {
            var buffers = _commandBuffers.Select(s => s.VkObjectNative).ToArray().AsSpan();
            foreach (var item in commandBuffers)
            {
                // Will call FreeCommandBuffer and then removes the element from array
                item.Dispose();
            }

        }

        public void ResetCommandPool(CommandPoolResetFlags flags = 0)
        {
            RenderingContext.Vk.ResetCommandPool(_context.Device, _vkObject, flags);
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
                    FreeCommandBuffers(_commandBuffers.ToArray());
                    // Should be already empty at that moment, but let it be for now
                    _commandBuffers.Clear();
                    RenderingContext.Vk.DestroyCommandPool(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkCommandPool>());
                }

                _disposed = true;
            }
        }
    }
}