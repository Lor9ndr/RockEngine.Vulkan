using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanSurfaceBuilder
    {
        private readonly InstanceWrapper _instance;
        private readonly Vk _api;
        private readonly IWindow _window;

        public VulkanSurfaceBuilder(InstanceWrapper instance, Vk api, IWindow window)
        {
            _instance = instance;
            _api = api;
            _window = window;
        }

        public unsafe SurfaceWrapper Build()
        {
            if(_window.VkSurface is null)
            {
                throw new Exception("Unable to create surface");
            }
            var handle = _window.VkSurface.Create<AllocationCallbacks>(_instance.Instance.ToHandle(), null);
            var khrSurface = new KhrSurface(_api.Context);
            return new SurfaceWrapper(_api, _instance, new SurfaceKHR(handle.Handle), khrSurface);
        }
    }
}