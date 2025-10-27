using RockEngine.Core.Assets;
using RockEngine.Core.Rendering.Materials;

namespace RockEngine.Core.ResourceProviders
{
    public class MaterialProvider : IResourceProvider<Material>
    {
        private readonly object _source;
        private readonly Func<ValueTask<Material>> _getter;

        public bool IsAssetBased => _source is AssetReference<MaterialAsset>;

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

        // Helper properties for serialization
        public AssetReference<MaterialAsset> AssetReference => _source as AssetReference<MaterialAsset>;
        public Material DirectMaterial => _source as Material;
    }
}
