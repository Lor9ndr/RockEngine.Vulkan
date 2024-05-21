using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;
using Silk.NET.Windowing;

namespace RockEngine.Vulkan.VulkanInitilizers
{
    internal class VulkanContext:IDisposable
    {
        public Vk Api { get; private set; }
        public VulkanInstance Instance { get; private set; }
        public VulkanLogicalDevice Device { get; private set; }
        public VulkanSurface Surface { get; private set; }

        private readonly IWindow _window;

        public VulkanContext(IWindow window)
        {
            _window = window;
        }

        public void Initialize()
        {
            Api = Vk.GetApi();
            CreateInstance();
            CreateSurface();
            CreateDevice();
        }
        private void CreateInstance()
        {
            // Implement instance creation logic
        }

        private void CreateSurface()
        {
            // Implement surface creation logic
        }

        private void CreateDevice()
        {
            // Implement device creation logic
        }

        public void Dispose()
        {
            // Implement disposal logic
        }
    }
}
