using System.IO.MemoryMappedFiles;

namespace RockEngine.Assets
{

    public class MemoryMappedLoadStrategy : IAssetLoadStrategy
    {
        public bool CanHandle(long fileSize) => fileSize > 1024 * 1024;

        public async Task<AssetHeader> LoadMetadataAsync(string filePath, IAssetSerializer serializer)
        {
            var fileInfo = new FileInfo(filePath);
            using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, fileInfo.Length, MemoryMappedFileAccess.Read);

            return await serializer.DeserializeHeaderAsync(stream);
        }

        public async Task LoadDataAsync<T>(IAsset<T> asset, string filePath, IAssetSerializer serializer) where T : class
        {
            await LoadDataAsync(asset, typeof(T), filePath, serializer);
        }

        public async Task LoadDataAsync(IAsset asset, Type dataType, string filePath, IAssetSerializer serializer)
        {
            var fileInfo = new FileInfo(filePath);
            using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, fileInfo.Length, MemoryMappedFileAccess.Read);

            var data = await serializer.DeserializeDataAsync(stream, dataType);
            asset.SetData(data);
        }

        public async Task<IAsset> LoadAssetAsync(string filePath, AssetHeader assetHeader, IAssetSerializer serializer)
        {
            var fileInfo = new FileInfo(filePath);
            using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, fileInfo.Length, MemoryMappedFileAccess.Read);

            var path = new AssetPath(filePath);
            return await serializer.DeserializeAssetAsync(stream, path);
        }
    }
}