using MessagePack;

using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.Rendering.Materials;

namespace RockEngine.Core.ResourceProviders
{
    [MessagePackObject]
    public class MaterialProvider : IResourceProvider<Material>
    {
        [IgnoreMember]

        private readonly object _source;
        [IgnoreMember]

        private readonly Func<ValueTask<Material>> _getter;
        [IgnoreMember]
        public bool IsAssetBased => _source is AssetReference<MaterialAsset>;

        // Helper properties for serialization
        [Key(2)]
        public AssetReference<MaterialAsset> AssetReference => _source as AssetReference<MaterialAsset>;

        [IgnoreMember]
        public Material DirectMaterial => _source as Material;

        // For assets
        public MaterialProvider(AssetReference<MaterialAsset> assetRef)
        {
            _source = assetRef;
            _getter = async () =>
            {
                var asset = await assetRef.GetAssetAsync();
                return await asset.GetAsync();
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
            var result = await _getter();
            return result;
        }

    }
}
