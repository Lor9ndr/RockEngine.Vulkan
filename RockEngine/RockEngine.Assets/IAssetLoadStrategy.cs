namespace RockEngine.Assets
{
    /// <summary>
    /// Updated IAssetLoadStrategy interface to use AssetHeader
    /// </summary>
    public interface IAssetLoadStrategy
    {
        bool CanHandle(long fileSize);
        Task<AssetHeader> LoadHeaderAsync(string filePath, IAssetSerializer serializer);
        Task<IAsset> LoadAssetAsync(string filePath, IAssetSerializer serializer);
        Task LoadDataAsync<T>(IAsset<T> asset, string filePath, IAssetSerializer serializer) where T : class;
        Task LoadDataAsync(IAsset asset, Type dataType, string filePath, IAssetSerializer serializer);
    }
}