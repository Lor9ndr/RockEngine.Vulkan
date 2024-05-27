using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanCommandPoolBuilder : DisposableBuilder
    {
        private readonly Vk _api;
        private readonly LogicalDeviceWrapper _device;
        private CommandPoolCreateFlags _flags;
        private uint _queueFamilyIndex;

        public VulkanCommandPoolBuilder(Vk api, LogicalDeviceWrapper device)
        {
            _api = api;
            _device = device;
        }

        public VulkanCommandPoolBuilder WithFlags(CommandPoolCreateFlags flags)
        {
            _flags = flags;
            return this;
        }

        public VulkanCommandPoolBuilder WithQueueFamilyIndex(uint value)
        {
            _queueFamilyIndex = value;
            return this;
        }

        public CommandPoolWrapper Build()
        {
            CommandPoolCreateInfo ci = new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = _flags,
                QueueFamilyIndex = _queueFamilyIndex
            };
            unsafe
            {
                _api.CreateCommandPool(_device.Device, in ci, null, out CommandPool cp)
                    .ThrowCode("Failed to create command pool");
                return new CommandPoolWrapper(_api, _device, cp);
            }

        }
    }
}
