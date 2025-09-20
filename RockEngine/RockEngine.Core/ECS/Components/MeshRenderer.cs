using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.Attributes;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public class MeshRenderer : Component, IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        public AssetReference<MeshAsset> MeshAsset { get; set;}

        public AssetReference<MaterialAsset> Material { get; set; }


        [SerializeIgnore]
        public IMesh Mesh => MeshAsset.Asset;

        [SerializeIgnore]
        public bool HasIndices => Mesh.HasIndices;
        [SerializeIgnore]
        public uint? IndicesCount => Mesh.IndicesCount;
        [SerializeIgnore]
        public uint VerticesCount => Mesh.VerticesCount;

        public MeshRenderer()
        {
        }

        public void SetAssets(AssetReference<MeshAsset> mesh, AssetReference<MaterialAsset> materialAsset)
        {
            MeshAsset = mesh;
            Material = materialAsset;
            World.GetCurrent().EnqueueForStart(this);
        }

        public override async ValueTask OnStart(Renderer renderer)
        {
            if(Mesh is not null && Material is not null)
            {
                if(Mesh is IGpuResource meshResource)
                {
                    await meshResource.LoadGpuResourcesAsync();
                }
                await Material.Asset.LoadGpuResourcesAsync();
                renderer.Draw(this);
            }
            else
            {
                _logger.Warn("Attempt to start mesh renderer without asset or material");
            }
        }

        public void Dispose()
        {
        }
    }
}
