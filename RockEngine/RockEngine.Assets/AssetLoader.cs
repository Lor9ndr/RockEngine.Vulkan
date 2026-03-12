using System.Collections.Concurrent;

namespace RockEngine.Assets
{
    public class AssetLoader : IAssetLoader
    {
        private readonly IAssetSerializer _serializer;
        private readonly List<IAssetLoadStrategy> _loadStrategies;
        private string _basePath = string.Empty;
        private readonly ConcurrentDictionary<Guid, string> _idToPathMap = new();

        public AssetLoader(
            IAssetSerializer serializer,
            MemoryMappedLoadStrategy mmapStrategy,
            StreamLoadStrategy streamStrategy)
        {
            _serializer = serializer;
            _loadStrategies = new List<IAssetLoadStrategy> { mmapStrategy, streamStrategy };
        }

        public void SetBasePath(string basePath)
        {
            _basePath = basePath;
            BuildIdToPathMap();
        }

        private string GetFullPath(string assetPath) =>
            Path.Combine(_basePath, assetPath);

        private IAssetLoadStrategy GetLoadStrategy(string filePath)
        {
            var fileInfo = new FileInfo(filePath);

            foreach (var strategy in _loadStrategies)
            {
                if (strategy.CanHandle(fileInfo.Length))
                    return strategy;
            }

            return _loadStrategies.Last(); // Fallback
        }

        private void BuildIdToPathMap()
        {
            if (string.IsNullOrEmpty(_basePath) || !Directory.Exists(_basePath))
                return;

            var assetFiles = Directory.GetFiles(_basePath, "*.asset", SearchOption.AllDirectories);

            Parallel.ForEach(assetFiles, file =>
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var header = _serializer.DeserializeHeaderAsync(stream).GetAwaiter().GetResult();
                    var relativePath = Path.GetRelativePath(_basePath, file);
                    _idToPathMap[header.AssetId] = relativePath;
                }
                catch (Exception ex)
                {
                    // Log warning but continue
                    Console.WriteLine($"Failed to read header from {file}: {ex.Message}");
                }
            });
        }

        public async Task<IAsset> LoadAssetAsync(Guid assetId)
        {
            if (!_idToPathMap.TryGetValue(assetId, out var path))
                throw new FileNotFoundException($"Asset with ID {assetId} not found in index");

            return await LoadAssetAsync(path);
        }

        public async Task<IAsset> LoadAssetAsync(string assetPath)
        {
            var fullPath = GetFullPath(assetPath);
            var strategy = GetLoadStrategy(fullPath);

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var asset = await _serializer.DeserializeAssetAsync(stream, new AssetPath(assetPath));

            // Update ID to path map
            _idToPathMap[asset.ID] = assetPath;

            return asset;
        }

        public async Task<T> LoadAssetAsync<T>(Guid assetId) where T : class, IAsset
        {
            var asset = await LoadAssetAsync(assetId);
            return asset as T ?? throw new InvalidCastException($"Asset is not of type {typeof(T).Name}");
        }

        public async Task<T> LoadAssetAsync<T>(string assetPath) where T : class, IAsset
        {
            var asset = await LoadAssetAsync(assetPath);
            return asset as T ?? throw new InvalidCastException($"Asset is not of type {typeof(T).Name}");
        }

        public async Task LoadAssetDataAsync<T>(IAsset<T> asset) where T : class
        {
            await LoadAssetDataAsync(asset, typeof(T));
        }

        public async Task LoadAssetDataAsync(IAsset asset, Type dataType)
        {
            if (asset.IsDataLoaded)
                return;

            var fullPath = GetFullPath(asset.Path.ToString());
            var strategy = GetLoadStrategy(fullPath);

            await strategy.LoadDataAsync(asset, dataType, fullPath, _serializer);
        }
    }
}