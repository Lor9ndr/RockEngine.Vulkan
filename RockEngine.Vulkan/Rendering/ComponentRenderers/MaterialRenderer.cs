using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class MaterialRenderer : IComponentRenderer<Material>
    {
        public async ValueTask InitializeAsync(Material component, VulkanContext context)
        {
            context.PipelineManager.SetTexture(component.Texture, 0, 1);
        }

        public async Task RenderAsync(Material component, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
        }
        public void Dispose()
        {
        }
    }
}
