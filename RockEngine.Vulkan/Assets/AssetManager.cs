using RockEngine.Vulkan.ECS;

using System.Text.Json;

namespace RockEngine.Vulkan.Assets
{
    public class AssetManager
    {
        private readonly List<IAsset> _loadedAssets = new List<IAsset>();

        private JsonSerializerOptions GetSerializerOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new AssetConverter(this));
            options.Converters.Add(new AssetEnumerableConverter(this));
            options.WriteIndented = true;
            return options;
        }

        public async Task<T?> LoadAssetAsync<T>(string path, CancellationToken cancellationToken = default) where T : IAsset
        {
            await using FileStream fs = new FileStream(path, FileMode.Open);
            var options = GetSerializerOptions();
            var asset = await JsonSerializer.DeserializeAsync<T>(fs, options, cancellationToken: cancellationToken)
                ?? throw new Exception($"Failed to load an asset with path: {path}");
            _loadedAssets.Add(asset);
            return asset;
        }

        public async Task<T?> LoadAssetByIdAsync<T>(Guid id, string path, CancellationToken cancellationToken = default) where T : IAsset
        {
            var asset = _loadedAssets.FirstOrDefault(a => a.ID == id && a.Path == path);
            if (asset != null)
            {
                return (T)asset;
            }

            return await LoadAssetAsync<T>(path, cancellationToken);
        }

        public async Task SaveAssetAsync<T>(T asset, CancellationToken cancellationToken = default) where T : IAsset
        {
            await using var fs = File.OpenWrite(asset.Path);
            var options = GetSerializerOptions();
            await JsonSerializer.SerializeAsync(fs, asset, options, cancellationToken: cancellationToken);
            asset.IsChanged = false;
        }

        public async Task<Project> CreateProjectAsync(string name, string path, CancellationToken cancellationToken = default)
        {
            Project p = new Project(name, path);
            await SaveAssetAsync(p, cancellationToken)
                .ConfigureAwait(false);
            return p;
        }
    }
}