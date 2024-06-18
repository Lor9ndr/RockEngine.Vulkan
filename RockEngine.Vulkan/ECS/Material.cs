using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public class Material : Component, IRenderableComponent<Material>, IDisposable
    {
        private IComponentRenderer<Material> _renderer;
        private readonly Texture _texture;

        public override int Order => 0;

        public IComponentRenderer<Material> Renderer => _renderer;

        public Texture Texture => _texture;

        public Material(Entity entity, Texture t) 
            : base(entity)
        {
            _texture = t;
        }


        public override async Task OnInitializedAsync(VulkanContext context)
        {
            _renderer = IoC.Container.GetRenderer<Material>();
            await _renderer.InitializeAsync(this, context)
                .ConfigureAwait(false);
            IsInitialized = true;
        }

        public Task RenderAsync(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            return _renderer.RenderAsync(this, context, commandBuffer);
        }

        public void Dispose()
        {
            _texture.Dispose();
        }
    }
}
