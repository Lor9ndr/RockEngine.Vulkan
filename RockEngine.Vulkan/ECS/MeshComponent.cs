using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Rendering.ComponentRenderers.Factories;
using RockEngine.Vulkan.VkObjects;

using SimpleInjector.Lifestyles;

namespace RockEngine.Vulkan.ECS
{
    public class MeshComponent : Component, IRenderableComponent<MeshComponent>, IDisposable
    {
        private MeshAsset _asset;
        private MeshComponentRenderer _renderer;

        public Vertex[] Vertices => _asset.Vertices;
        public uint[]? Indices => _asset.Indices;

        public IComponentRenderer<MeshComponent> Renderer => _renderer;

        public int Order => 100;

        public MeshComponent()
        {
        }

        public void SetAsset(MeshAsset meshAsset)
        {
            _asset = meshAsset;
        }

        /// <summary>
        /// Initializing the meshComponent, create/get renderer 
        /// </summary>
        /// <returns>Task as async function</returns>
        public override async Task OnInitializedAsync()
        {
            ArgumentNullException.ThrowIfNull(_asset);

            using (var scope = AsyncScopedLifestyle.BeginScope(IoC.Container))
            {
                var factory = scope.GetInstance<MeshComponentRendererFactory>();
                _renderer = factory.Get(this);
            }
            await _renderer.InitializeAsync(this).ConfigureAwait(false);
            IsInitialized = true;
        }

        public Task RenderAsync(CommandBufferWrapper commandBuffer)
        {
            if (!IsInitialized)
            {
                return Task.CompletedTask;
            }
            return _renderer.RenderAsync(this, commandBuffer);
        }

        public void Dispose()
        {
            _renderer.Dispose();
        }
    }
}