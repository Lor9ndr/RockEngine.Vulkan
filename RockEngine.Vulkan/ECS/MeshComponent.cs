using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Rendering.MaterialRendering;

namespace RockEngine.Vulkan.ECS
{
    public class MeshComponent : Component, IRenderableComponent<MeshComponent>, IDisposable
    {
        private MeshAsset _asset;
        private IComponentRenderer<MeshComponent> _renderer;

        public Material Material { get; private set; }
        public Vertex[] Vertices => _asset.Vertices;
        public uint[]? Indices => _asset.Indices;

        public IComponentRenderer<MeshComponent> Renderer => _renderer;

        public int Order => 99999;


        public MeshComponent(IComponentRenderer<MeshComponent> renderer)
        {
            _renderer = renderer;
        }

        public void SetAsset(MeshAsset meshAsset)
        {
            _asset = meshAsset;
        }

        public void SetMaterial(Material material)
        {
            Material = material;
        }

        /// <summary>
        /// Initializing the meshComponent, create/get renderer 
        /// </summary>
        /// <returns>Task as async function</returns>
        public override async Task OnInitializedAsync()
        {
            ArgumentNullException.ThrowIfNull(_asset);

            await _renderer.InitializeAsync(this).ConfigureAwait(false);
            IsInitialized = true;
        }

        public ValueTask RenderAsync(FrameInfo frameInfo)
        {
            if (!IsInitialized)
            {
                return default;
            }
            return _renderer.RenderAsync(this, frameInfo);
        }

        public override ValueTask UpdateAsync(double time)
        {
            return _renderer.UpdateAsync(this);
        }

        public void Dispose()
        {
            _renderer.Dispose();
        }
    }
}