﻿using Silk.NET.Vulkan;


namespace RockEngine.Vulkan
{
    public class VkCommandPool : VkObject<CommandPool>
    {
        private readonly VulkanContext _context;
        private readonly List<VkCommandBuffer> _commandBuffers = new List<VkCommandBuffer>();
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
        public int OwnerThreadId => _ownerThreadId;

        public VkCommandPool(VulkanContext context, CommandPool commandPool)
            : base(commandPool)
        {
            _context = context;
        }

        public static unsafe VkCommandPool Create(VulkanContext context, in CommandPoolCreateInfo ci)
        {
            VulkanContext.Vk.CreateCommandPool(context.Device, in ci, in VulkanContext.CustomAllocator<VkCommandPool>(), out var commandPool);
            return new VkCommandPool(context, commandPool);
        }

        public static unsafe VkCommandPool Create(VulkanContext context, CommandPoolCreateFlags flags, uint queueFamilyIndex)
        {
            CommandPoolCreateInfo ci = new CommandPoolCreateInfo(StructureType.CommandPoolCreateInfo,
                                                                 flags: flags,
                                                                 queueFamilyIndex: queueFamilyIndex);

            return Create(context, in ci);
        }
        internal void ReturnCommandBuffer(VkCommandBuffer cmd)
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
                throw new InvalidOperationException("Command buffer returned to wrong thread pool");

                _commandBuffers.Add(cmd);
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
            Vk.AllocateCommandBuffers(_context.Device, in allocInfo, &cbNative)
              .VkAssertResult("Failed to allocate command buffer");
            //Debugger.Log(1, "Allocation", $"Allocated a command buffer with handle: {cbNative.Handle}");
            var cb = new VkCommandBuffer(_context, in cbNative, this, allocInfo.Level == CommandBufferLevel.Secondary);
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

            CommandBuffer[] commandBuffers = new CommandBuffer[(int)count];
                unsafe
                {
                    fixed (CommandBuffer* pCommandBuffers = commandBuffers)
                    {
                        VulkanContext.Vk.AllocateCommandBuffers(_context.Device, ref allocateInfo, pCommandBuffers);
                    }
                }
                return commandBuffers.ToArray().Select(s => new VkCommandBuffer(_context, in s, this, level == CommandBufferLevel.Secondary)).ToArray();
          
        }

        public void Reset()
        {

                Vk.ResetCommandPool(_context.Device, this, CommandPoolResetFlags.ReleaseResourcesBit)
                    .VkAssertResult("Failed to reset Command pool");
            
        }

        public unsafe void FreeCommandBuffer(VkCommandBuffer commandBuffer)
        {

            var buffer = commandBuffer.VkObjectNative;

                VulkanContext.Vk.FreeCommandBuffers(_context.Device, _vkObject, 1, &buffer);
           

            commandBuffer.Dispose();
            _commandBuffers.Remove(commandBuffer);
        }

        public void FreeCommandBuffers(VkCommandBuffer[] commandBuffers)
        {
            foreach (var item in commandBuffers)
            {
                // Will call FreeCommandBuffer and then removes the element from array
                item.Dispose();
            }

        }

       
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.CommandPool, name);

        protected override unsafe void Dispose(bool disposing)
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
                        VulkanContext.Vk.DestroyCommandPool(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkCommandPool>());
                }

                _disposed = true;
            }
        }
    }
}