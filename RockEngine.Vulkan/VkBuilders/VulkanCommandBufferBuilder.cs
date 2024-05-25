using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.GLFW;
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanCommandBufferBuilder : DisposableBuilder
    {
        private CommandBufferLevel _level;
        private readonly VulkanContext _context;

        public VulkanCommandBufferBuilder(VulkanContext context)
        {
            _context = context;
        }

        public VulkanCommandBufferBuilder WithLevel(CommandBufferLevel level)
        {
            _level = level; 
            return this;
        }

        public VulkanCommandBuffer Build()
        {
            var createdCommandPool = _context.GetOrCreateCommandPool();
            CommandBufferAllocateInfo ai = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = createdCommandPool.CommandPool,
                Level = _level,
                CommandBufferCount = 1,
            };
            _context.Api.AllocateCommandBuffers(_context.Device.Device, in ai, out CommandBuffer cb)
                .ThrowCode("Failed to allocate command buffer");
            return new VulkanCommandBuffer(_context, cb, createdCommandPool);
        }
        public VulkanCommandBuffer[] Build(uint count)
        {
            var commandPool = _context.GetOrCreateCommandPool();
            ReadOnlySpan<CommandBufferAllocateInfo> refAi = [new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool.CommandPool,
                Level = _level,
                CommandBufferCount = count,
            } ];

            Span<CommandBuffer> commandBuffersArr = stackalloc CommandBuffer[(int)count];

            _context.Api.AllocateCommandBuffers(_context.Device.Device, refAi, commandBuffersArr)
                .ThrowCode("Failed to allocate command buffer");

            VulkanCommandBuffer[] vulkanCommandBuffers = new VulkanCommandBuffer[count];
            for (int i = 0; i < commandBuffersArr.Length; i++)
            {
                vulkanCommandBuffers[i] = new VulkanCommandBuffer(_context, commandBuffersArr[i], commandPool);
            }

            return vulkanCommandBuffers;
        }
    }
}
