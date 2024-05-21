using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanSemaphoreBuilder : DisposableBuilder
    {
        private readonly Vk _api;
        private readonly VulkanLogicalDevice _device;

        public VulkanSemaphoreBuilder(Vk api, VulkanLogicalDevice device)
        {
            _api = api;
            _device = device;
        }

        public VulkanSemaphore Build()
        {
            SemaphoreCreateInfo ci = new SemaphoreCreateInfo()
            {
                SType = StructureType.SemaphoreCreateInfo
            };
            unsafe
            {
                _api.CreateSemaphore(_device.Device, in ci, null, out var semaphore)
                    .ThrowCode("Failed to create semaphore");
                return new VulkanSemaphore(_api, _device, semaphore);
            }
        }
    }
}
