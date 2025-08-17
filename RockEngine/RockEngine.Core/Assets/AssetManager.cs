using NLog;

using RockEngine.Core.Assets.RockEngine.Core.Assets;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;

using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace RockEngine.Core.Assets
{
    public sealed class AssetManager : IAssetStorage, IDisposable
    {

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private const string ProjectExtension = ".rockproj";

        private readonly struct AssetRecord(IAsset asset)
        {
            public readonly IAsset Asset = asset;
        }

        private readonly IAssetSerializer _serializer;
        private readonly AssetFactoryRegistry _factoryRegistry;
        private readonly AssimpLoader _assimpLoader;
        private readonly ConcurrentDictionary<Guid, AssetRecord> _assetsById = new();
        private readonly ConcurrentDictionary<string, AssetRecord> _assetsByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _assetLocks = new(StringComparer.OrdinalIgnoreCase);
        private FrozenSet<string> _assetFiles = FrozenSet<string>.Empty;
        private string _basePath = string.Empty;

        public string BasePath => _basePath;

        public AssetManager(IAssetSerializer serializer, AssetFactoryRegistry factoryRegistry, AssimpLoader assimpLoader)
        {
            _logger.Info("AssetManager initializing...");
            _serializer = serializer;
            _factoryRegistry = factoryRegistry;
            _assimpLoader = assimpLoader;
        }

        private void Initialize(ProjectAsset project)
        {
            _basePath = project.Data.RootPath;
            IndexProjectAssets();
        }

        private void IndexProjectAssets()
        {
            var files = Directory.GetFiles(_basePath, "*.asset", SearchOption.AllDirectories);
            _assetFiles = files.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }

        public async Task<ProjectAsset> CreateProjectAsync(string projectName, string basePath)
        {
            var projectDir = Path.Combine(basePath, projectName);
            var projectPath = new AssetPath(projectDir, projectName, ProjectExtension);

            var project = Create<ProjectAsset>(projectPath);
            project.SetData(new ProjectData { Name = projectName, RootPath = projectDir });

            RegisterAsset(project);
            Initialize(project);
            await SaveAsync(project);

            return project;
        }

        public async Task<ModelAsset> LoadModelAsync(string filePath, string? modelName = null, string parentPath = "Models")
        {
            modelName ??= Path.GetFileNameWithoutExtension(filePath);
            var modelPathKey = NormalizePath(Path.Combine(parentPath, modelName));

            if (_assetsByPath.TryGetValue(modelPathKey, out var existing))
                return (ModelAsset)existing.Asset;

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
                    TextureAssetIDs = new List<Guid>(textureIDs)
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

        public T Create<T>(AssetPath path, string? name = null) where T : IAsset =>
            (T)Create(typeof(T), path, name);

        public IAsset Create(Type assetType, AssetPath path, string? name = null)
        {
            var asset = (IAsset)IoC.Container.GetInstance(assetType);
            asset.Path = path;
            asset.Name = name;
            RegisterAsset(asset);
            return asset;
        }

        public async Task<ProjectAsset> LoadProjectAsync(string projectName, string basePath)
        {
            var projectFile = Path.Combine(basePath, projectName, $"{projectName}{ProjectExtension}");
            var project = (ProjectAsset)await LoadFullAsync<ProjectData>(new AssetPath(projectFile), true, default);
            Initialize(project);
            return project;
        }

        public async Task SaveAsync<T>(IAsset<T> asset) where T : class =>
            await SaveAsync(asset, GetFullPath(asset.Path));

        private async Task SaveAsync<T>(IAsset<T> asset, string fullPath) where T : class
        {
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
            await assetLock.WaitAsync().ConfigureAwait(false);

            try
            {
                asset.UpdateModified();
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var tempPath = Path.ChangeExtension(fullPath, ".tmp");
                using var buffer = MemoryPool<byte>.Shared.Rent(8192);
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Memory.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await _serializer.SerializeAsync(asset, stream).ConfigureAwait(false);
                }

                File.Move(tempPath, fullPath, true);
            }
            finally
            {
                assetLock.Release();
            }
        }

        public async Task<IAsset> LoadMetadataAsync(AssetPath path)
        {
            var fullPath = GetFullPath(path);
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
            await assetLock.WaitAsync().ConfigureAwait(false);

            // TODO: THINK TO USE MemoryMappedFiles and so on
            try
            {
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                return await _serializer.DeserializeMetadataAsync(stream).ConfigureAwait(false);
            }
            finally
            {
                assetLock.Release();
            }
        }

        public async Task<IAsset<T>> LoadFullAsync<T>(
            AssetPath path,
            bool loadDependencies = true,
            CancellationToken ct = default) where T : class
        {
            var pathKey = NormalizePath(path.ToString());
            if (_assetsByPath.TryGetValue(pathKey, out var record))
                return (IAsset<T>)record.Asset;

            var fullPath = GetFullPath(path);
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
            await assetLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var asset = await _serializer.DeserializeMetadataAsync(stream).ConfigureAwait(false);
                stream.Position = 0;
                await LoadDataForAssetAsync(asset, stream).ConfigureAwait(false);

                RegisterAsset(asset);

                if (loadDependencies && asset.Dependencies.Any())
                {
                    var dependencyTasks = new Task<IAsset?>[asset.Dependencies.Count()];
                    for (int i = 0; i < asset.Dependencies.Count(); i++)
                    {
                        dependencyTasks[i] = FindDependencyAsync(asset.Dependencies.ElementAt(i).ID, ct);
                    }

                    await Task.WhenAll(dependencyTasks).ConfigureAwait(false);

                    for (int i = 0; i < dependencyTasks.Length; i++)
                    {
                        var dependency = await dependencyTasks[i];
                        if (dependency != null)
                        {
                            asset.AddDependency(dependency);
                        }
                    }
                }

                return (IAsset<T>)asset;
            }
            finally
            {
                assetLock.Release();
            }
        }

        private async Task LoadDataForAssetAsync(IAsset asset, Stream stream)
        {
            if (asset.IsDataLoaded) return;

            var data = await _serializer.DeserializeDataAsync(stream, asset.GetDataType());
            asset.SetData(data);
        }

        private async Task<IAsset?> FindDependencyAsync(Guid depId, CancellationToken ct)
        {
            if (_assetsById.TryGetValue(depId, out var record))
                return record.Asset;

            foreach (var file in _assetFiles)
            {
                ct.ThrowIfCancellationRequested();
                var assetLock = _assetLocks.GetOrAdd(file, _ => new SemaphoreSlim(1, 1));
                await assetLock.WaitAsync(ct).ConfigureAwait(false);

                try
                {
                    using var stream = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    var metadata = await _serializer.DeserializeMetadataAsync(stream).ConfigureAwait(false);
                    if (metadata.ID == depId)
                    {
                        stream.Position = 0;
                        await LoadDataForAssetAsync(metadata, stream).ConfigureAwait(false);
                        RegisterAsset(metadata);
                        return metadata;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Skipping corrupt asset file: {file}");
                }
                finally
                {
                    assetLock.Release();
                }
            }

            return null;
        }

        private void RegisterAsset(IAsset asset)
        {
            var pathKey = NormalizePath(asset.Path.ToString());
            var record = new AssetRecord(asset);

            _assetsByPath[pathKey] = record;
            _assetsById[asset.ID] = record;
        }

        private string GetFullPath(AssetPath assetPath) =>
            Path.Combine(_basePath, assetPath.ToString());

        private static string NormalizePath(string path) =>
            Path.GetFullPath(path)
                .Replace('\\', '/')
                .TrimEnd('/')
                .ToLowerInvariant();

        public void Dispose()
        {
            foreach (var lockObj in _assetLocks.Values)
            {
                lockObj.Dispose();
            }
        }

        // Interface implementations
        public async Task<IAsset<TData>> LoadAsync<TAsset, TData>(AssetPath path)
            where TAsset : IAsset<TData>
            where TData : class =>
            await LoadFullAsync<TData>(path, true, default);

        public async Task<IAsset<T>> LoadAsync<T>(AssetPath path) where T : class =>
            await LoadFullAsync<T>(path, true, default);

        public async Task SaveProjectAsync(ProjectAsset project) =>
            await SaveAsync(project);

        public bool Exists(AssetPath path) =>
            File.Exists(GetFullPath(path));

        public T? GetAsset<T>(Guid assetID) where T : class, IAsset =>
            _assetsById.TryGetValue(assetID, out var record) ? (T)record.Asset : null;
    }
}