using RockEngine.Vulkan.ECS;

using System.Text.Json;

namespace RockEngine.Vulkan.Assets
{
    public class AssetManager
    {
        private readonly List<IAsset> _loadedAssets = new List<IAsset>();
        private JsonSerializerOptions _options;

        public AssetManager()
        {
            _options = new JsonSerializerOptions();
            _options.Converters.Add(new AssetConverter(this));
            _options.Converters.Add(new AssetEnumerableConverter(this));
            _options.IncludeFields = true;
            _options.IgnoreReadOnlyFields = true;
            _options.IgnoreReadOnlyProperties = true;
            _options.WriteIndented = true;
        }


        public async Task<T?> LoadAssetAsync<T>(string path, CancellationToken cancellationToken = default) where T : IAsset
        {
            await using FileStream fs = new FileStream(path, FileMode.Open,FileAccess.Read);
            var asset = await JsonSerializer.DeserializeAsync<T>(fs, _options, cancellationToken: cancellationToken) ?? throw new Exception($"Failed to load an asset with path: {path}");
            _loadedAssets.Add(asset);
            return asset;
        }

        private void TryAddAsset<T>(T asset) where T : IAsset
        {
            if (_loadedAssets.Contains(asset))
            {
                return;
            }

            _loadedAssets.Add(asset);
        }

        public async Task<T?> GetAssetByIdAsync<T>(Guid id, string path, CancellationToken cancellationToken = default) where T : IAsset
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
            await JsonSerializer.SerializeAsync(fs, asset, _options, cancellationToken: cancellationToken);
            asset.IsChanged = false;
            TryAddAsset(asset);
        }

        public async Task<Project> CreateProjectAsync(string name, string path, CancellationToken cancellationToken = default)
        {
            Project p = new Project(name, path);
            await SaveAssetAsync(p, cancellationToken)
                .ConfigureAwait(false);
            return p;
        }

        /// <summary>
        /// Will change the path of the asset to the <see cref="Project.AssetPath"/> + <see cref="IAsset.Name"/> + <see cref="IAsset.FILE_EXTENSION"/>
        /// and save it to that path
        /// </summary>
        /// <typeparam name="T">asset type</typeparam>
        /// <param name="project">to which project to add asset</param>
        /// <param name="asset">asset that will be moved to the project assets path</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task AddAssetToProject<T>(Project project, T asset, CancellationToken cancellationToken = default) where T:IAsset
        {
            asset.Path = project.AssetPath + "/" + asset.Name + IAsset.FILE_EXTENSION;
            return SaveAssetAsync(asset, cancellationToken);
        }
    }
}