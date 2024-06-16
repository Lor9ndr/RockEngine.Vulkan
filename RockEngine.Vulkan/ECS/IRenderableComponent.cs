using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public interface IRenderableComponent<T> : IRenderable where T : Component
    {
        public IComponentRenderer<T> Renderer { get; }
    }
    public interface IRenderable
    {
        public Task RenderAsync(VulkanContext context, CommandBufferWrapper commandBuffer);
    }
}
