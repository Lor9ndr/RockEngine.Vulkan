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
        public int Order { get; }
        public Task RenderAsync(CommandBufferWrapper commandBuffer);
    }
}
