using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class MaterialRenderer : IComponentRenderer<MaterialComponent>
    {
        private readonly VulkanContext _context;

        public MaterialRenderer(VulkanContext context)
        {
            _context = context;
        }

        public async ValueTask InitializeAsync(MaterialComponent component)
        {
            _context.PipelineManager.SetTexture(component.Texture, 2, 0);
        }

        public async Task RenderAsync(MaterialComponent component, CommandBufferWrapper commandBuffer)
        {
            // SOme usage of shader from material
            _context.PipelineManager.Use(component.Texture, commandBuffer);
        }

        public void Dispose()
        {
        }
    }
}