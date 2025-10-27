using RockEngine.Core.ResourceProviders;

namespace RockEngine.Core.Assets
{
    public class AssetResourceProvider<TAsset, TRuntime> : IResourceProvider<TRuntime>
     where TAsset : class, IAsset, IResourceProvider<TRuntime>
    {
        private readonly AssetReference<TAsset> _assetRef;

        public AssetReference<TAsset> AssetRef => _assetRef;
        public AssetResourceProvider(AssetReference<TAsset> assetRef) => _assetRef = assetRef;


        public async ValueTask<TRuntime> GetAsync() => await AssetRef.Asset.GetAsync();
    }
}
