using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class MaterialRenderer : IComponentRenderer<Material>
    {
        public async ValueTask InitializeAsync(Material component, VulkanContext context)
        {
            context.PipelineManager.SetTexture(component.Texture, 2, 0);
        }

        public async Task RenderAsync(Material component, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            context.PipelineManager.Use(component.Texture, commandBuffer);
        }
        public void Dispose()
        {
        }
    }
}