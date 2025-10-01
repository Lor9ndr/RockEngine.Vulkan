using NLog;

using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Assets.RockEngine.Core.Assets;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;

using System.Collections.Concurrent;
using System.Text;

namespace RockEngine.Core.Assets
{
    public sealed class AssetManager :  IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private const string ProjectExtension = ".rockproj";
        private const string AssetExtension = ".asset";
        private const string MetaExtension = ".meta";
        private const int MetaFileBufferSize = 4096;
        private sealed class AssetMetadata
        {
            public Guid ID { get; set; }
            public string Name { get; set; }
            public AssetPath Path { get; set; }
            public Type AssetType { get; set; }
            public DateTime LastModified { get; set; }

            public AssetMetadata() { }

            public AssetMetadata(IAsset asset)
            {
                ID = asset.ID;
                Name = asset.Name;
                Path = asset.Path;
                AssetType = asset.GetType();
                LastModified = DateTime.UtcNow;
            }

            public byte[] ToBytes()
            {
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms, Encoding.UTF8);

                writer.Write(ID.ToByteArray());
                writer.Write(Name);
                writer.Write(Path.ToString());
                writer.Write(AssetType.AssemblyQualifiedName);
                writer.Write(LastModified.ToBinary());

                return ms.ToArray();
            }

            public static AssetMetadata FromBytes(byte[] data)
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms, Encoding.UTF8);

                var id = new Guid(reader.ReadBytes(16));
                var name = reader.ReadString();
                var pathStr = reader.ReadString();
                var assetType = Type.GetType(reader.ReadString());
                var lastModified = DateTime.FromBinary(reader.ReadInt64());

                return new AssetMetadata
                {
                    ID = id,
                    Name = name,
                    Path = new AssetPath(pathStr),
                    AssetType = assetType,
                    LastModified = lastModified
                };
            }
        }

        private readonly IAssetSerializer _serializer;
        private readonly AssimpLoader _assimpLoader;

        // Metadata storage
        private readonly ConcurrentDictionary<Guid, AssetMetadata> _metadataById = new();
        private readonly ConcurrentDictionary<string, AssetMetadata> _metadataByPath = new(StringComparer.OrdinalIgnoreCase);

        // In-memory asset storage
        private readonly ConcurrentDictionary<Guid, IAsset> _assetsById = new();
        private readonly ConcurrentDictionary<string, IAsset> _assetsByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _assetLocks = new(StringComparer.OrdinalIgnoreCase);

        private string _basePath = string.Empty;
        private ProjectAsset _project;

        public string BasePath => _basePath;
        public event Action<Guid>? OnAssetRemoved;
        public event Action<IAsset>? OnAssetRegistered;
        public event Action? OnAssetsChanged;

        public AssetManager(IAssetSerializer serializer, AssimpLoader assimpLoader)
        {
            _logger.Info("AssetManager initializing...");
            _serializer = serializer;
            _assimpLoader = assimpLoader;
        }

        private async Task InitializeAsync(ProjectAsset project)
        {
            _basePath = project.Data.RootPath;
            _project = project;

            await IndexProjectAssetsAsync();
        }

        private async Task IndexProjectAssetsAsync()
        {
            // Process meta files
            var metaFiles = Directory.GetFiles(_basePath, $"*{MetaExtension}", SearchOption.AllDirectories);
            var tasks = new List<Task>(metaFiles.Length);

            foreach (var metaFile in metaFiles)
            {
                tasks.Add(ProcessMetaFileAsync(metaFile));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Check for assets without meta files
            var assetFiles = Directory.GetFiles(_basePath, $"*{AssetExtension}", SearchOption.AllDirectories);
            var missingMetaTasks = new List<Task>();

            foreach (var assetFile in assetFiles)
            {
                var metaFile = GetMetaPath(assetFile);
                if (!File.Exists(metaFile))
                {
                    missingMetaTasks.Add(CreateMetaFromAssetAsync(assetFile));
                }
            }

            await Task.WhenAll(missingMetaTasks).ConfigureAwait(false);

            // Load all assets into memory
            var loadTasks = new List<Task>();
            foreach (var metadata in _metadataById.Values)
            {
                if (_assetsById.ContainsKey(metadata.ID))
                {
                    continue;
                }
                loadTasks.Add(LoadAssetInternalAsync(metadata));
            }
            await Task.WhenAll(loadTasks).ConfigureAwait(false);

            _logger.Info($"Loaded {_metadataById.Count} assets into memory");
        }

        private async Task ProcessMetaFileAsync(string metaFilePath)
        {
            try
            {
                using var stream = new FileStream(metaFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buffer = new byte[MetaFileBufferSize];
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, MetaFileBufferSize));
                var metadata = AssetMetadata.FromBytes(buffer.AsSpan(0, bytesRead).ToArray());

                var normalizedPath = NormalizePath(metadata.Path.ToString());
                _metadataByPath[normalizedPath] = metadata;
                _metadataById[metadata.ID] = metadata;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Skipping corrupt meta file: {metaFilePath}");
            }
        }

        private async Task CreateMetaFromAssetAsync(string assetFilePath)
        {
            try
            {
                using var stream = new FileStream(assetFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var asset = await _serializer.DeserializeMetadataAsync(stream);
                await SaveMetadataAsync(asset);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to create meta file for asset: {assetFilePath}");
            }
        }

        private async Task SaveMetadataAsync(IAsset asset)
        {
            var metadata = new AssetMetadata(asset);
            var metaPath = GetMetaPath(GetFullPath(asset.Path));
            var directory = Path.GetDirectoryName(metaPath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await SaveMetadataToDiskAsync(metadata);

            var normalizedPath = NormalizePath(asset.Path.ToString());
            _metadataByPath[normalizedPath] = metadata;
            _metadataById[asset.ID] = metadata;
        }

        public async Task<ProjectAsset> CreateProjectAsync(string projectName, string basePath)
        {
            var projectDir = Path.Combine(basePath, projectName);
            Directory.CreateDirectory(projectDir);

            var projectPath = new AssetPath(projectDir, projectName, ProjectExtension);
            var project = Create<ProjectAsset>(projectPath);
            project.SetData(new ProjectData { Name = projectName, RootPath = projectDir });

            _basePath = projectDir;
            _project = project;

            RegisterAsset(project);
            await SaveAsync(project);
            await InitializeAsync(project);

            return project;
        }

        public async Task<ModelAsset> LoadModelAsync(string filePath, string? modelName = null, string parentPath = "Models")
        {
            modelName ??= Path.GetFileNameWithoutExtension(filePath);
            var modelPathKey = NormalizePath(Path.Combine(parentPath, modelName));

            if (TryGetAsset<ModelAsset>(modelPathKey, out var existing))
                return existing;

            var meshesData = await _assimpLoader.LoadMeshesAsync(filePath);
            var modelAsset = Create<ModelAsset>(new AssetPath(parentPath, modelName));

            var saveTasks = new List<Task>(meshesData.Count * 3 + 1);
            var textureCache = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);

            foreach (var meshData in meshesData)
            {
                var meshName = !string.IsNullOrEmpty(meshData.Name) ? meshData.Name : $"Mesh_{Guid.NewGuid()}";
                var meshAsset = Create<MeshAsset>(
                    new AssetPath($"{parentPath}/{modelName}/Meshes", meshName),
                    meshName);

                meshAsset.SetGeometry(meshData.Vertices, meshData.Indices);

                var materialAsset = Create<MaterialAsset>(
                    new AssetPath($"{parentPath}/{modelName}/Materials", meshName),
                    meshName);

                var textureIDs = new Guid[meshData.Textures.Count];
                for (int i = 0; i < meshData.Textures.Count; i++)
                {
                    var texturePath = meshData.Textures[i];
                    if (!textureCache.TryGetValue(texturePath, out var textureAsset))
                    {
                        var textureName = Path.GetFileName(texturePath);
                        textureAsset = Create<TextureAsset>(
                            new AssetPath($"{parentPath}/{modelName}/Textures", textureName));
                        textureAsset.SetData(new TextureData
                        {
                            FilePaths = [texturePath],
                            GenerateMipmaps = true,
                            Type = TextureType.Texture2D
                        });
                        textureCache[texturePath] = textureAsset;
                        RegisterAsset(textureAsset);
                        saveTasks.Add(SaveAsync(textureAsset));
                    }
                    textureIDs[i] = textureAsset.ID;
                }

                materialAsset.SetData(new MaterialData
                {
                    PipelineName = "Geometry",
                    Textures = [.. textureIDs.Select(s => new AssetReference<TextureAsset>(s))]
                });

                modelAsset.AddPart(new ModelPart { Mesh = meshAsset, Material = materialAsset });

                RegisterAsset(meshAsset);
                RegisterAsset(materialAsset);
                saveTasks.Add(SaveAsync(meshAsset));
                saveTasks.Add(SaveAsync(materialAsset));
            }

            RegisterAsset(modelAsset);
            saveTasks.Add(SaveAsync(modelAsset));
            await Task.WhenAll(saveTasks);

            return modelAsset;
        }
        public MaterialAsset CreateMaterial(string name, string template, List<AssetReference<TextureAsset>>? textures = null, Dictionary<string, object>? parameters = null)
        {
            var material = Create<MaterialAsset>(new AssetPath("Materials", name));
            material.SetData(new MaterialData
            {
                PipelineName = template,
                Textures = textures ?? new List<AssetReference<TextureAsset>>(),
                Parameters = parameters
            });
            return material;
        }

        public async Task<MaterialAsset> CreateAndSaveMaterial(string name, string template, List<AssetReference<TextureAsset>>? textures = null, Dictionary<string, object>? parameters = null)
        {
            var material = CreateMaterial(name, template, textures, parameters);
            await SaveAsync(material);
            return material;
        }

        public T Create<T>(AssetPath path, string? name = null) where T : IAsset
        {
            var asset = (T)IoC.Container.GetInstance(typeof(T));
            asset.Path = path;
            asset.Name = name ?? Path.GetFileNameWithoutExtension(path.FullPath);
            RegisterAsset(asset);
            return asset;
        }

        public async Task<ProjectAsset> LoadProjectAsync(string projectName, string basePath)
        {
            var projectFile = Path.Combine(basePath, projectName, $"{projectName}{ProjectExtension}");
            return await LoadProjectAsync(projectFile);
        }

        public async Task<ProjectAsset> LoadProjectAsync(string projectPath)
        {
            var project = await LoadAssetFromDisk<ProjectData>(new AssetPath(projectPath));
            _basePath = project.Data.RootPath;
            await IndexProjectAssetsAsync();
            return (ProjectAsset)project;
        }

        private async Task<IAsset<T>> LoadAssetFromDisk<T>(AssetPath path) where T : class
        {
            var fullPath = GetFullPath(path);
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
            await assetLock.WaitAsync();

            try
            {
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var asset = await _serializer.DeserializeMetadataAsync(stream);
                stream.Position = 0;
                await LoadDataForAssetAsync(asset, stream);
                RegisterAsset(asset);
                return (IAsset<T>)asset;
            }
            finally
            {
                assetLock.Release();
            }
        }

        public async Task SaveAsync(IAsset asset) 
        {
            asset.BeforeSaving();
            await SaveAsync(asset, GetFullPath(asset.Path));
            asset.AfterSaving();
            await SaveMetadataAsync(asset);
        }

        private async Task SaveAsync(IAsset asset, string fullPath) 
        {
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
            await assetLock.WaitAsync();

            try
            {
                asset.UpdateModified();
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await _serializer.SerializeAsync(asset, stream);
            }
            finally
            {
                assetLock.Release();
            }
        }

        private async Task LoadAssetInternalAsync(AssetMetadata metadata)
        {
            var fullPath = GetFullPath(metadata.Path);
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            var asset = await _serializer.DeserializeMetadataAsync(stream);
            stream.Position = 0;
            await LoadDataForAssetAsync(asset, stream);
            RegisterAsset(asset);
        }

        private async Task LoadDataForAssetAsync(IAsset asset, Stream stream)
        {
            if (asset.IsDataLoaded) return;

            var data = await _serializer.DeserializeDataAsync(stream, asset.GetDataType());
            asset.SetData(data);
        }

        public void RegisterAsset(IAsset asset)
        {
            var metadata = new AssetMetadata(asset);
            var pathKey = NormalizePath(asset.Path.ToString());
            if (!string.IsNullOrEmpty(pathKey))
            {
                _metadataByPath[pathKey] = metadata;
                _assetsByPath[pathKey] = asset;
            }

            _metadataById[asset.ID] = metadata;
            _assetsById[asset.ID] = asset;

            OnAssetRegistered?.Invoke(asset);
            OnAssetsChanged?.Invoke();
        }

        public bool TryGetAsset<T>(Guid assetID, out T asset) where T : class, IAsset
        {
            if (_assetsById.TryGetValue(assetID, out var foundAsset) && foundAsset is T typedAsset)
            {
                asset = typedAsset;
                return true;
            }

            asset = null!;
            return false;
        }

        public bool TryGetAsset<T>(string pathKey, out T asset) where T : class, IAsset
        {
            var normalizedPath = NormalizePath(pathKey);
            if (_assetsByPath.TryGetValue(normalizedPath, out var foundAsset) && foundAsset is T typedAsset)
            {
                asset = typedAsset;
                return true;
            }

            asset = null!;
            return false;
        }

        private string GetFullPath(AssetPath assetPath)
        {
            if (Path.IsPathRooted(assetPath.ToString()))
                return assetPath.ToString();

            return Path.Combine(_basePath, assetPath.ToString());
        }

        private string GetMetaPath(string assetPath) => assetPath + MetaExtension;

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        public void Dispose()
        {
            foreach (var lockObj in _assetLocks.Values)
            {
                lockObj.Dispose();
            }
        }

        public async Task<IAsset<TData>> LoadAsync<TAsset, TData>(AssetPath path) where TAsset : IAsset<TData> where TData : class
        {
            var pathKey = NormalizePath(path.ToString());
            if (_assetsByPath.TryGetValue(pathKey, out var asset) && asset is IAsset<TData> typedAsset)
            {
                return typedAsset;
            }
            return await LoadAssetFromDisk<TData>(path);
        }

        public async Task<IAsset<T>> LoadAsync<T>(AssetPath path) where T : class
        {
            var pathKey = NormalizePath(path.ToString());
            if (_assetsByPath.TryGetValue(pathKey, out var asset) && asset is IAsset<T> typedAsset)
            {
                return typedAsset;
            }
            return await LoadAssetFromDisk<T>(path);
        }

        public async Task SaveProjectAsync(ProjectAsset project) => await SaveAsync(project);

        public bool Exists(AssetPath path) => _metadataByPath.ContainsKey(NormalizePath(path.ToString()));

        public T? GetAsset<T>(Guid assetID) where T : class, IAsset
        {
            return _assetsById.TryGetValue(assetID, out var asset) ? asset as T : null;
        }

        public async Task<T?> LoadAssetByIdAsync<T>(Guid assetId, CancellationToken ct = default) where T : class, IAsset
        {
            if (TryGetAsset(assetId, out T? asset))
                return asset;

            if (!_metadataById.TryGetValue(assetId, out var metadata))
            {
                _logger.Warn($"Metadata not found for asset ID: {assetId}");
                return null;
            }

            try
            {
                return (T?)await LoadAssetFromDisk<object>(metadata.Path);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load asset with ID {assetId}");
                throw;
            }
        }

        public AssetMetadataInfo? GetMetadataByPath(string assetPath)
        {
            var normalizedPath = NormalizePath(assetPath);
            if (_metadataByPath.TryGetValue(normalizedPath, out var metadata))
            {
                return new AssetMetadataInfo(metadata.ID, metadata.Name, metadata.AssetType.Name);
            }
            return null;
        }

        public AssetMetadataInfo? GetMetadataById(Guid assetId)
        {
            if (_metadataById.TryGetValue(assetId, out var metadata))
            {
                return new AssetMetadataInfo(metadata.ID, metadata.Name, metadata.AssetType.Name);
            }
            return null;
        }

        public async Task<bool> RemoveAssetAsync(Guid assetId, bool removeFile = false)
        {
            if (!_metadataById.TryGetValue(assetId, out var metadata))
            {
                _logger.Warn($"Attempted to remove non-existent asset with ID: {assetId}");
                return false;
            }

            return await RemoveAssetInternalAsync(metadata, removeFile);
        }

        public async Task<bool> RenameAssetAsync(Guid assetId, string newName)
        {
            if (!_metadataById.TryGetValue(assetId, out var metadata))
                return false;

            if (_assetsById.TryGetValue(assetId, out var asset))
            {
                asset.Name = newName;
            }

            var newPath = new AssetPath(metadata.Path.Folder, newName, metadata.Path.Extension);
            return await MoveAssetInternalAsync(metadata, newPath);
        }
        private async Task<bool> MoveAssetInternalAsync(AssetMetadata metadata, AssetPath newPath)
        {
            var newNormalizedPath = NormalizePath(newPath.ToString());
            var oldNormalizedPath = NormalizePath(metadata.Path.ToString());

            if (oldNormalizedPath.Equals(newNormalizedPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (_metadataByPath.ContainsKey(newNormalizedPath))
                return false;

            var oldFullPath = GetFullPath(metadata.Path);
            var newFullPath = GetFullPath(newPath);

            var oldLock = _assetLocks.GetOrAdd(oldFullPath, _ => new SemaphoreSlim(1, 1));
            var newLock = _assetLocks.GetOrAdd(newFullPath, _ => new SemaphoreSlim(1, 1));

            await oldLock.WaitAsync();
            await newLock.WaitAsync();

            try
            {
                if (File.Exists(oldFullPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newFullPath));
                    File.Move(oldFullPath, newFullPath);
                }

                var oldMetaPath = GetMetaPath(oldFullPath);
                if (File.Exists(oldMetaPath))
                {
                    File.Delete(oldMetaPath);
                }

                metadata.Path = newPath;
                metadata.LastModified = DateTime.UtcNow;

                if (_assetsById.TryGetValue(metadata.ID, out var asset))
                {
                    asset.Path = newPath;
                }

                _metadataByPath.TryRemove(oldNormalizedPath, out _);
                _metadataByPath[newNormalizedPath] = metadata;

                _assetsByPath.TryRemove(oldNormalizedPath, out _);
                if (_assetsById.TryGetValue(metadata.ID, out var loadedAsset))
                {
                    _assetsByPath[newNormalizedPath] = loadedAsset;
                }

                await SaveMetadataToDiskAsync(metadata);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to move asset from {oldNormalizedPath} to {newNormalizedPath}");
                return false;
            }
            finally
            {
                oldLock.Release();
                newLock.Release();
            }
        }
        private async Task SaveMetadataToDiskAsync(AssetMetadata metadata)
        {
            var metaPath = GetMetaPath(GetFullPath(metadata.Path));
            var directory = Path.GetDirectoryName(metaPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var stream = new FileStream(metaPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var metadataBytes = metadata.ToBytes();
            await stream.WriteAsync(metadataBytes);
        }

        public async Task<bool> MoveAssetAsync(string currentAssetPath, AssetPath newPath)
        {
            var normalizedPath = NormalizePath(currentAssetPath);
            if (!_metadataByPath.TryGetValue(normalizedPath, out var metadata))
                return false;

            return await MoveAssetInternalAsync(metadata, newPath);
        }
        public async Task<bool> MoveAssetAsync(Guid assetId, AssetPath newPath)
        {
            if (!_metadataById.TryGetValue(assetId, out var metadata))
                return false;

            return await MoveAssetInternalAsync(metadata, newPath);
        }
        public async Task<bool> RemoveAssetAsync(string assetPath, bool removeFile = false)
        {
            var normalizedPath = NormalizePath(assetPath);
            if (!_metadataByPath.TryGetValue(normalizedPath, out var metadata))
            {
                _logger.Warn($"Attempted to remove non-existent asset with path: {assetPath}");
                return false;
            }

            return await RemoveAssetInternalAsync(metadata, removeFile);
        }

        private async Task<bool> RemoveAssetInternalAsync(AssetMetadata metadata, bool removeFile)
        {
            try
            {
                _metadataById.TryRemove(metadata.ID, out _);
                _metadataByPath.TryRemove(NormalizePath(metadata.Path.ToString()), out _);
                _assetsById.TryRemove(metadata.ID, out _);
                _assetsByPath.TryRemove(NormalizePath(metadata.Path.ToString()), out _);

                if (removeFile)
                {
                    var fullPath = GetFullPath(metadata.Path);
                    var metaPath = GetMetaPath(fullPath);

                    if (File.Exists(fullPath))
                        File.Delete(fullPath);

                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                }

                OnAssetRemoved?.Invoke(metadata.ID);
                OnAssetsChanged?.Invoke();

                _logger.Info($"Removed asset: {metadata.Name} (ID: {metadata.ID})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to remove asset: {metadata.Name} (ID: {metadata.ID})");
                return false;
            }
        }

        public void ClearCache()
        {
            _assetsById.Clear();
            _assetsByPath.Clear();
            _logger.Info("Asset cache cleared");
        }

        public void RemoveFromCache(Guid assetId)
        {
            if (_metadataById.TryGetValue(assetId, out var metadata))
            {
                _assetsById.TryRemove(assetId, out _);
                _assetsByPath.TryRemove(NormalizePath(metadata.Path.ToString()), out _);
                _logger.Debug($"Removed asset {assetId} from cache");
            }
        }

        public void RemoveFromCache(string assetPath)
        {
            var normalizedPath = NormalizePath(assetPath);
            if (_metadataByPath.TryGetValue(normalizedPath, out var metadata))
            {
                RemoveFromCache(metadata.ID);
            }
        }


    }
}
