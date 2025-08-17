using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public class MeshRenderer : Component, IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        public IMeshProvider Mesh { get; private set;}
        public MaterialAsset Material { get; private set; }

        public bool HasIndices => Mesh.HasIndices;
        public uint? IndicesCount => Mesh.IndicesCount;
        public uint VerticesCount => Mesh.VerticesCount;

        public MeshRenderer()
        {
        }

        public void SetAssets(IMeshProvider mesh, MaterialAsset materialAsset)
        {
            Mesh = mesh;
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
                await Material.LoadGpuResourcesAsync();
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
