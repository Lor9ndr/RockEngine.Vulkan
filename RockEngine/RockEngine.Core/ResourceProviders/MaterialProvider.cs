using MemoryPack;
using RockEngine.Core.Assets;
using RockEngine.Core.Rendering.Materials;

namespace RockEngine.Core.ResourceProviders
{
    [MemoryPackable]
    public partial class MaterialProvider : IResourceProvider<Material>
    {
        private readonly object _source;
        private readonly Func<ValueTask<Material>> _getter;
        
        public bool IsAssetBased => _source is AssetReference<MaterialAsset>;

        // Helper properties for serialization
        public AssetReference<MaterialAsset>? AssetReference => _source as AssetReference<MaterialAsset>;

        [MemoryPackIgnore]
        public Material? DirectMaterial => _source as Material;

        [MemoryPackConstructor]
        public MaterialProvider(AssetReference<MaterialAsset> assetReference)
        {
            _source = assetReference;
            _getter = async () =>
            {
                var asset = await assetReference.GetAssetAsync().ConfigureAwait(false);
                return await asset.GetAsync().ConfigureAwait(false);
            };
        }

        // For direct objects
        public MaterialProvider(Material material)
        {
            _source = material;
            _getter = () => ValueTask.FromResult(material);
        }

        public async ValueTask<Material> GetAsync()
        {
            var result = await _getter().ConfigureAwait(false);
            return result;
        }

    }
}
