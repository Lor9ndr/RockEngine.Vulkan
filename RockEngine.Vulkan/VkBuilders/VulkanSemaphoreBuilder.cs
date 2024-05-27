using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanSemaphoreBuilder : DisposableBuilder
    {
        private readonly Vk _api;
        private readonly LogicalDeviceWrapper _device;

        public VulkanSemaphoreBuilder(Vk api, LogicalDeviceWrapper device)
        {
            _api = api;
            _device = device;
        }

        public SemaphoreWrapper Build()
        {
            SemaphoreCreateInfo ci = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo
            };
            unsafe
            {
                _api.CreateSemaphore(_device.Device, in ci, null, out var semaphore)
                    .ThrowCode("Failed to create semaphore");
                return new SemaphoreWrapper(_api, _device, semaphore);
            }
        }
    }
}
