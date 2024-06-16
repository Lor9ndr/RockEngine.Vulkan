using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Rendering.ComponentRenderers.Factories;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using SimpleInjector.Lifestyles;

namespace RockEngine.Vulkan.ECS
{
    public class MeshComponent : Component, IRenderableComponent<MeshComponent>, IDisposable
    {
        private readonly RenderableObject _renderableObject;
        private MeshComponentRenderer _renderer;

        public Vertex[] Vertices => _renderableObject.Vertices;
        public uint[]? Indicies => _renderableObject.Indicies;

        public IComponentRenderer<MeshComponent> Renderer => _renderer;

        public override int Order => 100;

        public MeshComponent(Entity entity, Vertex[] vertices, uint[]? indices = null)
            :base(entity)
        {
            _renderableObject = new RenderableObject(vertices, indices);
        }

        public override async Task OnInitializedAsync(VulkanContext context)
        {
            using (AsyncScopedLifestyle.BeginScope(IoC.Container))
            {
                var factory = IoC.Container.GetInstance<MeshComponentRendererFactory>();
                _renderer = factory.Get(this);
                await _renderer.InitializeAsync(this, context).ConfigureAwait(false);
            }
            IsInitialized = true;
        }

        public async Task RenderAsync(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            if (!IsInitialized)
            {
                return;
            }
            await _renderer.RenderAsync(this, context, commandBuffer);
        }

        public void Dispose()
        {
            _renderer.Dispose();
        }
    }
}