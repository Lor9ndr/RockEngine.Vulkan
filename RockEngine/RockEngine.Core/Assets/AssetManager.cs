using System.Collections.Concurrent;

using Newtonsoft.Json.Linq;

using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Assets.Factories;
using RockEngine.Core.Assets.Registres;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.Registries;

namespace RockEngine.Core.Assets;

public class AssetManager : IDisposable
{
   /* private readonly IRegistry<Type> _typeRegistry;
    private readonly IRegistry<IAssetSerializer<IAssetData>> _serializerRegistry;
    private readonly IRegistry<IAssetFactory<IAssetData, IAsset>> _factoryRegistry;*/

    private readonly ConcurrentDictionary<Guid, IAsset> _assets = new();
    private readonly ConcurrentDictionary<string, Guid> _pathToGuid = new();
    private readonly ConcurrentDictionary<Guid, string> _guidToPath = new();
    private readonly ConcurrentDictionary<Guid, HashSet<Guid>> _dependencies = new();
    private readonly ConcurrentDictionary<Guid, HashSet<Guid>> _reverseDependencies = new();
    private readonly ConcurrentDictionary<string, Task<IAsset>> _loadingTasks = new();
    private readonly ConcurrentDictionary<Guid, int> _refCounts = new();

    public AssetManager(
        /*IRegistry<Type, > typeRegistry,
        IRegistry<IAssetSerializer<IAssetData>> serializerRegistry,
        IRegistry<IAssetFactory<IAssetData, IAsset>> factoryRegistry*/)
    {/*
        _typeRegistry = typeRegistry;
        _serializerRegistry = serializerRegistry;
        _factoryRegistry = factoryRegistry;*/
    }

    private async Task<T> LoadAsync<T>(string path) where T : class, IAsset
    {
        if (_pathToGuid.TryGetValue(path, out var existingId))
        {
            return (T)_assets[existingId];
        }

        if (_loadingTasks.TryGetValue(path, out var existingTask))
        {
            return (T)await existingTask;
        }

        Task<IAsset> loadTask = LoadAssetInternal<T>(path); // Fixed type
        _loadingTasks[path] = loadTask;

        try
        {
            var asset = (T)await loadTask; // Cast to T
            return asset;
        }
        finally
        {
            _loadingTasks.TryRemove(path, out _);
        }
    }

    private async Task<IAsset> LoadAssetInternal<T>(string path) where T : class, IAsset
    {
        throw new NotImplementedException();
        /* if (!File.Exists(path))
         {
             throw new FileNotFoundException($"Asset file not found: {path}");
         }

         var json = await File.ReadAllTextAsync(path);
         var jObject = JObject.Parse(json);
         var typeIdentifier = jObject["$type"]?.Value<string>();

         if (string.IsNullOrEmpty(typeIdentifier))
         {
             throw new InvalidOperationException("Asset type identifier is missing");
         }

         if (!_typeRegistry.TryGet(typeIdentifier, out var dataType))
         {
             throw new InvalidOperationException($"Unregistered asset type: {typeIdentifier}");
         }

         if (!_serializerRegistry.TryGet(dataType, out var serializer))
         {
             throw new InvalidOperationException($"No serializer found for {dataType.Name}");
         }

         if (!_factoryRegistry.TryGet(dataType, out var factory))
         {
             throw new InvalidOperationException($"No factory found for {dataType.Name}");
         }

         var data = serializer.Deserialize(json);
         var asset = factory.CreateAsset(path, data);

         // Store before loading to prevent circular dependencies
         _assets[asset.ID] = asset;
         _pathToGuid[path] = asset.ID;
         _guidToPath[asset.ID] = path;
         _refCounts[asset.ID] = 1; // Initial reference

         // Handle dependencies
         if (asset is ISerializableAsset<IAssetData> serializableAsset)
         {
             var dependencies = new HashSet<Guid>();
             await ProcessDependencies(serializableAsset.GetData(), asset.ID, dependencies);
             _dependencies[asset.ID] = dependencies;
         }

         await asset.LoadAsync();
         return asset;*/
    }

    public async Task SaveAsync(IAsset asset, string? path = null)
    {
        throw new NotImplementedException();
/*
        var savePath = path ?? asset.Path;
        if (string.IsNullOrEmpty(savePath))
        {
            throw new ArgumentException("Path must be provided for saving");
        }

        if (asset is not ISerializableAsset<IAssetData> serializableAsset)
        {
            throw new InvalidOperationException("Asset doesn't support serialization");
        }

        var data = serializableAsset.GetData();
        var dataType = data.GetType();

        if (!((AssetTypeRegistry)_typeRegistry).TryGetTypeIdentifier(dataType, out var typeIdentifier))
        {
            throw new InvalidOperationException($"No type identifier registered for {dataType.Name}");
        }

        if (!_serializerRegistry.TryGet(dataType, out var serializer))
        {
            throw new InvalidOperationException($"No serializer found for {dataType.Name}");
        }

        var json = serializer.Serialize(data);
        var jObject = JObject.Parse(json);
        jObject["$type"] = typeIdentifier;

        await File.WriteAllTextAsync(savePath, jObject.ToString());

        // Update tracking if path changed
        if (savePath != asset.Path)
        {
            _pathToGuid.TryRemove(asset.Path, out _);
            asset.Path = savePath;
            _pathToGuid[savePath] = asset.ID;
            _guidToPath[asset.ID] = savePath;
        }*/
    }

    public T GetAsset<T>(Guid id) where T : class, IAsset
    {
        return _assets.TryGetValue(id, out var asset) ? (T)asset : null;
    }

    public T GetAssetByPath<T>(string path) where T : class, IAsset
    {
        return _pathToGuid.TryGetValue(path, out var id) ? GetAsset<T>(id) : null;
    }

    public void AddReference(Guid assetId)
    {
        _refCounts.AddOrUpdate(assetId, 1, (id, count) => count + 1);
    }

    public void ReleaseReference(Guid assetId)
    {
        _refCounts.AddOrUpdate(assetId, 0, (id, count) => Math.Max(0, count - 1));
        if (_refCounts.TryGetValue(assetId, out var count) && count == 0)
        {
            Unload(assetId);
        }
    }

    public void Unload(Guid id)
    {
        if (!_assets.TryRemove(id, out var asset)) return;

        // Remove reverse dependencies
        if (_reverseDependencies.TryRemove(id, out var dependents))
        {
            foreach (var dependentId in dependents)
            {
                ReleaseReference(dependentId);
            }
        }

        // Remove from path mapping
        if (_guidToPath.TryRemove(id, out var path))
        {
            _pathToGuid.TryRemove(path, out _);
        }

        // Unload dependencies
        if (_dependencies.TryRemove(id, out var dependencies))
        {
            foreach (var dependencyId in dependencies)
            {
                ReleaseReference(dependencyId);
            }
        }

        // Unload the asset
        asset.Unload();
        asset.Dispose();
        _refCounts.TryRemove(id, out _);
    }

    public void Dispose()
    {
        foreach (var assetId in _assets.Keys)
        {
            Unload(assetId);
        }
        _assets.Clear();
        _pathToGuid.Clear();
        _guidToPath.Clear();
        _dependencies.Clear();
        _reverseDependencies.Clear();
        _loadingTasks.Clear();
        _refCounts.Clear();
    }

    private async Task ProcessDependencies(IAssetData data, Guid parentId, HashSet<Guid> dependencies)
    {
        switch (data)
        {
            case SceneData sceneData:
                foreach (var assetPath in sceneData.AssetDependencies)
                {
                    await LoadDependency(assetPath, parentId, dependencies);
                }
                break;

            case ProjectData projectData:
                foreach (var assetId in projectData.AssetIDs)
                {
                    await LoadDependency(assetId, parentId, dependencies);
                }
                break;

            case MaterialData materialData:
                foreach (var item in materialData.Properties)
                {
                    if (item.Value.Type == MaterialPropertyType.Texture)
                    {
                        if (item.Value.Value is string texturePath)
                        {
                            await LoadDependency(texturePath, parentId, dependencies);
                        }
                    }
                }
                break;

            // Add MeshData processing
            case MeshData meshData:
                foreach (var texturePath in meshData.TexturePaths)
                {
                    await LoadDependency(texturePath, parentId, dependencies);
                }
                break;
        }
    }
    private async Task LoadDependency(Guid dependencyId, Guid parentId, HashSet<Guid> dependencies)
    {
        if (!_guidToPath.TryGetValue(dependencyId, out var path))
        {
            throw new InvalidOperationException($"No path found for asset ID: {dependencyId}");
        }
        await LoadDependency(path, parentId, dependencies);
    }
    private async Task LoadDependency(string dependencyPath, Guid parentId, HashSet<Guid> dependencies)
    {
        try
        {
            var dependency = await LoadAsync<IAsset>(dependencyPath);
            dependencies.Add(dependency.ID);

            // Add reverse dependency
            _reverseDependencies.AddOrUpdate(dependency.ID,
                new HashSet<Guid> { parentId },
                (id, set) =>
                {
                    set.Add(parentId);
                    return set;
                });

            // Add reference count
            AddReference(dependency.ID);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load dependency: {dependencyPath}", ex);
        }
    }

    public async Task ReloadAsync(Guid id)
    {
        if (!_guidToPath.TryGetValue(id, out var path)) return;
        Unload(id);
        await LoadAsync<IAsset>(path);
    }
}