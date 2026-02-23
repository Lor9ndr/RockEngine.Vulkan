namespace RockEngine.Assets
{
    public interface IAssetManager
    {
        string BasePath { get;}

        IAsyncEnumerable<AssetInfo> DiscoverAssets(string directory = "");
        Task<T> GetAssetAsync<T>(Guid assetId) where T : class, IAsset;
        Task<T> LoadAssetAsync<T>(string assetPath) where T : class, IAsset;
        Task<IAsset> LoadAssetAsync(Guid assetId);
        Task<IAsset> LoadAssetAsync(string assetPath);
        Task LoadAssetDataAsync(IAsset asset);
        Task SaveAsync(IAsset asset);
    }
}