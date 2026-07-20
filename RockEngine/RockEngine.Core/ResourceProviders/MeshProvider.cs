using MemoryPack;

using RockEngine.Core.Assets;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Buffers;

namespace RockEngine.Core.ResourceProviders
{
    [MemoryPackable]
    public partial class MeshProvider : IResourceProvider<IMesh>
    {

        protected readonly object _source;
        
        protected Func<ValueTask<IMesh>> _getter;
        
        public bool IsAssetBased => _source is AssetReference<MeshAsset>;

        // Helper properties for serialization
        public AssetReference<MeshAsset>? AssetReference => _source as AssetReference<MeshAsset>;

        [SerializeIgnore, MemoryPackIgnore]
        public virtual IMesh? DirectMesh => _source as IMesh;

        [MemoryPackConstructor]
        public MeshProvider(AssetReference<MeshAsset> assetReference)
        {
            _source = assetReference;
            _getter = async () =>
            {
                var asset = await assetReference.GetAssetAsync().ConfigureAwait(false);
                return await asset.GetAsync().ConfigureAwait(false);
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
            var result = await _getter().ConfigureAwait(false);
            return result;
        }



    }

    public class MeshProvider<TVertex> : MeshProvider, IDisposable where TVertex : unmanaged, IVertex
    {
        protected IMesh? _loadedMesh;
        public Guid ID { get; } = Guid.NewGuid();

        
        public MeshProvider(MeshData<TVertex> meshData) : base(meshData)
        {
            _getter = async () =>
            {
                if (_loadedMesh != null)
                {
                    return _loadedMesh;
                }
                var globalGeometryBuffer = IoC.Container.GetInstance<GlobalGeometryBuffer>();

                // Add mesh to global buffer
                await globalGeometryBuffer.AddMeshAsync(ID, meshData.Vertices!, meshData.Indices!).ConfigureAwait(false);
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
