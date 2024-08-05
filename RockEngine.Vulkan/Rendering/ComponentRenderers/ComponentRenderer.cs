using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    public interface IComponentRenderer<in T> : IDisposable where T : Component
    {
        public ValueTask InitializeAsync(T component);
        public Task RenderAsync(T component, CommandBufferWrapper commandBuffer);
    }
}
