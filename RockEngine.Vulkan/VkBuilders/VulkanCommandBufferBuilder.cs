using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.GLFW;
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanCommandBufferBuilder : DisposableBuilder
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;
        private readonly VulkanCommandPool _pool;
        private CommandBufferLevel _level;

        public VulkanCommandBufferBuilder(Vk api, VulkanLogicalDevice device, VulkanCommandPool pool)
        {
            _api = api;
            _device = device;
            _pool = pool;
        }

        public VulkanCommandBufferBuilder WithLevel(CommandBufferLevel level)
        {
            _level = level; 
            return this;
        }

        public VulkanCommandBuffer Build()
        {
            CommandBufferAllocateInfo ai = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _pool.CommandPool,
                Level = _level,
                CommandBufferCount = 1,
            };
            _api.AllocateCommandBuffers(_device.Device, in ai, out CommandBuffer cb)
                .ThrowCode("Failed to allocate command buffer");
            return new VulkanCommandBuffer(_api, _device, cb);
        }
        public VulkanCommandBuffer[] Build(uint count)
        {
            ReadOnlySpan<CommandBufferAllocateInfo> refAi = [new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _pool.CommandPool,
                Level = _level,
                CommandBufferCount = count,
            } ];


            Span<CommandBuffer> commandBuffersArr = stackalloc CommandBuffer[(int)count];

            _api.AllocateCommandBuffers(_device.Device, refAi, commandBuffersArr)
                .ThrowCode("Failed to allocate command buffer");

            VulkanCommandBuffer[] vulkanCommandBuffers = new VulkanCommandBuffer[count];
            for (int i = 0; i < commandBuffersArr.Length; i++)
            {
                vulkanCommandBuffers[i] = new VulkanCommandBuffer(_api, _device, commandBuffersArr[i]);
            }

            return vulkanCommandBuffers;
        }
    }
}
