using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VkObjects.Infos.Texture;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class MaterialRenderer : IComponentRenderer<MaterialComponent>
    {
        private readonly VulkanContext _context;
        private readonly PipelineManager _pipelineManager;

        public MaterialRenderer(VulkanContext context, PipelineManager pipelineManager)
        {
            _context = context;
            _pipelineManager = pipelineManager;
        }

        public async ValueTask InitializeAsync(MaterialComponent component)
        {
            var loadTasks = component.Material.Textures
            .Where(t => t.TextureInfo is NotLoadedTextureInfo)
            .Select(t => t.LoadAsync(_context));

            await Task.WhenAll(loadTasks);

            _pipelineManager.SetMaterialDescriptors(component.Material);
        }

        public Task RenderAsync(MaterialComponent component, FrameInfo frameInfo)
        {
            _pipelineManager.Use(component.Material, frameInfo);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}