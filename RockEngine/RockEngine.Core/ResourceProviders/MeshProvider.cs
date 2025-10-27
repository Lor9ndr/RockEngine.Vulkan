using RockEngine.Core.Assets;
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Buffers;

namespace RockEngine.Core.ResourceProviders
{
    public class MeshProvider : IResourceProvider<IMesh>
    {
        protected readonly object _source;
        protected Func<ValueTask<IMesh>> _getter;
        public bool IsAssetBased => _source is AssetReference<MeshAsset>;

      
        public MeshProvider(AssetReference<MeshAsset> assetRef)
        {
            _source = assetRef;
            _getter = async () =>
            {
                var asset = await assetRef.GetAssetAsync();
                return await asset.GetAsync();
            };
        }

        // For direct objects
        public MeshProvider(IMesh mesh)
        {
            _source = mesh;
            _getter = () => ValueTask.FromResult(mesh);
        }

        protected MeshProvider(object source)
        {
            _source = source;
        }
      

        public async ValueTask<IMesh> GetAsync()
        {
            var result = await _getter();
            return result;
        }

        // Helper properties for serialization
        public AssetReference<MeshAsset> AssetReference => _source as AssetReference<MeshAsset>;
        [SerializeIgnore]
        public virtual IMesh DirectMesh => _source as IMesh;
      
    }

    public class MeshProvider<TVertex> : MeshProvider, IDisposable where TVertex : unmanaged, IVertex
    {
        protected IMesh? _loadedMesh;
        public Guid ID { get;} = Guid.NewGuid();
        public MeshProvider(MeshData<TVertex> meshData) :base(meshData)
        {
            _getter = async () =>
            {
                if(_loadedMesh != null)
                {
                    return _loadedMesh;
                }
                var globalGeometryBuffer = IoC.Container.GetInstance<GlobalGeometryBuffer>();

                // Add mesh to global buffer
                await globalGeometryBuffer.AddMeshAsync(ID, meshData.Vertices!, meshData.Indices!);
                _loadedMesh = new Mesh(ID, (uint)meshData.Indices.Length, (uint)meshData.Vertices.Length);
                return _loadedMesh;
            };
        }

        public void Dispose()
        {
            var globalGeometryBuffer = IoC.Container.GetInstance<GlobalGeometryBuffer>();
            globalGeometryBuffer.RemoveMesh(ID);
            GC.SuppressFinalize(this);
        }

        ~MeshProvider()
        {
            Dispose();
        }

    }
}
