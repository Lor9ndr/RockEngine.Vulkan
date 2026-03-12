
namespace RockEngine.Assets
{
    public class StreamLoadStrategy : IAssetLoadStrategy
    {
        private const int OptimalBufferSize = 65536;

        public StreamLoadStrategy()
        {
        }

        public bool CanHandle(long fileSize) => true; // Fallback strategy

        public async Task<AssetHeader> LoadMetadataAsync(string filePath, IAssetSerializer serializer)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan);

            return await serializer.DeserializeHeaderAsync(fileStream);
        }

        public async Task LoadDataAsync<T>(IAsset<T> asset, string filePath, IAssetSerializer serializer) where T : class
        {
            await LoadDataAsync(asset, typeof(T), filePath, serializer);
        }

        private static async Task LoadDataForAssetAsync(IAsset asset, Type dataType, Stream stream, IAssetSerializer serializer)
        {
            var data = await serializer.DeserializeDataAsync(stream, dataType);
            asset.SetData(data);
        }

        public async Task LoadDataAsync(IAsset asset, Type dataType, string filePath, IAssetSerializer serializer)
        {
            using var memoryStream = new MemoryStream();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan);

            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            await LoadDataForAssetAsync(asset, dataType, memoryStream, serializer);

        }

        public async Task<IAsset> LoadAssetAsync(string filePath, AssetHeader assetHeader, IAssetSerializer serializer)
        {
            using var memoryStream = new MemoryStream();

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan);

            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            var path = new AssetPath(filePath);
            return await serializer.DeserializeAssetAsync(memoryStream, path);
        }
    }
}