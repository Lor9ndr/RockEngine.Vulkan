using NLog;

using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Assets.RockEngine.Core.Assets;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;

using System.Collections.Concurrent;
using System.Text;
using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.ObjectPool;

namespace RockEngine.Core.Assets
{
    public sealed class AssetManager : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private const string ProjectExtension = ".rockproj";
        private const string AssetExtension = ".asset";
        private const string MetaExtension = ".meta";
        private const int MetaFileBufferSize = 8192;
        private const int OptimalBufferSize = 65536; // 64KB aligned with disk sectors
        private const long MaxCacheSize = 1024 * 1024 * 128; // 128MB limit

        // Memory cache for assets
        private readonly MemoryCache _assetCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = MaxCacheSize,
            CompactionPercentage = 0.25
        });

        // Object pools for common types
        private readonly ObjectPool<MemoryStream> _memoryStreamPool;
        private readonly ObjectPool<byte[]> _bufferPool;

        private sealed class AssetMetadata
        {
            public Guid ID { get; set; }
            public string Name { get; set; }
            public AssetPath Path { get; set; }
            public Type AssetType { get; set; }
            public DateTime LastModified { get; set; }
            public long FileSize { get; set; }
            public int AccessCount { get; set; }

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
                writer.Write(FileSize);

                return ms.ToArray();
            }

            public static AssetMetadata FromBytes(byte[] data)
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms, Encoding.UTF8);

                try
                {
                    var id = new Guid(reader.ReadBytes(16));
                    var name = reader.ReadString();
                    var pathStr = reader.ReadString();
                    var assetType = Type.GetType(reader.ReadString());
                    var lastModified = DateTime.FromBinary(reader.ReadInt64());

                    // Only read FileSize if there's more data available
                    long fileSize = 0;
                    if (ms.Position < ms.Length)
                    {
                        fileSize = reader.ReadInt64();
                    }

                    return new AssetMetadata
                    {
                        ID = id,
                        Name = name,
                        Path = new AssetPath(pathStr),
                        AssetType = assetType,
                        LastModified = lastModified,
                        FileSize = fileSize
                    };
                }
                catch (EndOfStreamException ex)
                {
                    throw new InvalidDataException("Meta file data is incomplete", ex);
                }
            }
        }

        // Memory-mapped files management
        private readonly ConcurrentDictionary<string, (MemoryMappedFile MappedFile, MemoryMappedViewAccessor Accessor)> _mappedFiles = new();
        private readonly ConcurrentDictionary<Guid, Lazy<Task<IAsset>>> _lazyAssets = new();
        private readonly PriorityQueue<AssetLoadRequest, int> _prefetchQueue = new();
        private readonly CancellationTokenSource _prefetchCancellation = new();
        private readonly Task _prefetchWorker;

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
        private FileSystemWatcher _fileWatcher;

        public string BasePath => _basePath;
        public event Action<Guid>? OnAssetRemoved;
        public event Action<IAsset>? OnAssetRegistered;
        public event Action? OnAssetsChanged;

        public AssetManager(IAssetSerializer serializer, AssimpLoader assimpLoader)
        {
            _logger.Info("AssetManager initializing...");
            _serializer = serializer;
            _assimpLoader = assimpLoader;

            // Initialize object pools
            _memoryStreamPool = new DefaultObjectPool<MemoryStream>(
                new MemoryStreamPooledObjectPolicy(),
                Environment.ProcessorCount );

            _bufferPool = new DefaultObjectPool<byte[]>(
                new BufferPooledObjectPolicy(OptimalBufferSize),
                Environment.ProcessorCount * 4);

            // Start background prefetch worker
            _prefetchWorker = StartPrefetchWorker();

        }

        private async Task StartPrefetchWorker()
        {
            while (!_prefetchCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(100, _prefetchCancellation.Token);
                    await ProcessPrefetchQueueAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ProcessPrefetchQueueAsync()
        {
            const int maxConcurrentLoads = 4;
            var loadTasks = new List<Task>();

            while (_prefetchQueue.TryDequeue(out var request, out _) &&
                   loadTasks.Count < maxConcurrentLoads)
            {
                loadTasks.Add(LoadAssetBackgroundAsync(request.AssetId));
            }

            await Task.WhenAll(loadTasks);
        }

        public void PrefetchAsset(Guid assetId, int priority = 0)
        {
            if (!_assetsById.ContainsKey(assetId) && _metadataById.ContainsKey(assetId))
            {
                _prefetchQueue.Enqueue(new AssetLoadRequest(assetId), priority);
                _logger.Debug($"Prefetch queued for asset: {assetId}");
            }
        }

        private async Task LoadAssetBackgroundAsync(Guid assetId)
        {
            try
            {
                if (!_metadataById.TryGetValue(assetId, out var metadata))
                {
                    return;
                }

                // Use lazy loading to avoid duplicate loads
                var lazyTask = _lazyAssets.GetOrAdd(assetId, id => new Lazy<Task<IAsset>>(
                    () => LoadAssetWithOptimizationsAsync(metadata),
                    LazyThreadSafetyMode.ExecutionAndPublication));

                await lazyTask.Value;
                _logger.Debug($"Background prefetch completed for asset: {assetId}");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Background prefetch failed for asset: {assetId}");
            }
        }

        private void SetupFileWatching()
        {
            try
            {
                _fileWatcher = new FileSystemWatcher(_basePath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = !string.IsNullOrEmpty(_basePath)
                };
                _fileWatcher.Changed += OnAssetFileChanged;
                _fileWatcher.Deleted += OnAssetFileDeleted;
                _fileWatcher.Renamed += OnAssetFileRenamed;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to setup file system watcher");
            }
        }

        private void OnAssetFileChanged(object sender, FileSystemEventArgs e)
        {
            var normalizedPath = NormalizePath(e.FullPath);

            // Invalidate cache when files change
            if (_metadataByPath.TryGetValue(normalizedPath, out var metadata))
            {
                _assetCache.Remove(metadata.ID);
                _assetsById.TryRemove(metadata.ID, out _);
                _assetsByPath.TryRemove(normalizedPath, out _);
                _lazyAssets.TryRemove(metadata.ID, out _);

                _logger.Debug($"Invalidated cache for modified asset: {normalizedPath}");
            }
        }

        private void OnAssetFileDeleted(object sender, FileSystemEventArgs e)
        {
            var normalizedPath = NormalizePath(e.FullPath);
            if (_metadataByPath.TryGetValue(normalizedPath, out var metadata))
            {
                RemoveFromCache(metadata.ID);
                _logger.Debug($"Removed deleted asset from cache: {normalizedPath}");
            }
        }

        private void OnAssetFileRenamed(object sender, RenamedEventArgs e)
        {
            var oldNormalizedPath = NormalizePath(e.OldFullPath);
            var newNormalizedPath = NormalizePath(e.FullPath);

            if (_metadataByPath.TryGetValue(oldNormalizedPath, out var metadata))
            {
                _metadataByPath.TryRemove(oldNormalizedPath, out _);
                _metadataByPath[newNormalizedPath] = metadata;

                if (_assetsByPath.TryRemove(oldNormalizedPath, out var asset))
                {
                    _assetsByPath[newNormalizedPath] = asset;
                }

                _logger.Debug($"Updated paths for renamed asset: {oldNormalizedPath} -> {newNormalizedPath}");
            }
        }

        private async Task InitializeAsync(ProjectAsset project)
        {
            _basePath = project.Data.RootPath;
            _project = project;
            SetupFileWatching();
            await IndexProjectAssetsAsync();
            GC.Collect();

            // Update file watcher with new base path
            if (_fileWatcher != null)
            {
                _fileWatcher.Path = _basePath;
                _fileWatcher.EnableRaisingEvents = true;
            }
        }

        private async Task IndexProjectAssetsAsync()
        {
            // Use optimized file enumeration
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                BufferSize = OptimalBufferSize,
                AttributesToSkip = FileAttributes.System | FileAttributes.Temporary
            };

            var metaFiles = Directory.EnumerateFiles(_basePath, $"*{MetaExtension}", enumerationOptions);

            // Process meta files in parallel with optimal degree of parallelism
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            await Parallel.ForEachAsync(metaFiles, options, async (metaFile, ct) =>
            {
                await ProcessMetaFileAsync(metaFile);
            });

            // Check for assets without meta files using parallel processing
            var assetFiles = Directory.EnumerateFiles(_basePath, $"*{AssetExtension}", enumerationOptions);
            var missingMetaTasks = new List<Task>();

            await Parallel.ForEachAsync(assetFiles, options, async (assetFile, ct) =>
            {
                var metaFile = GetMetaPath(assetFile);
                if (!File.Exists(metaFile))
                {
                    await CreateMetaFromAssetAsync(assetFile);
                }
            });

            // Prefetch frequently used assets in background
            PrefetchCommonAssets();
        }

        private void PrefetchCommonAssets()
        {
            // Prefetch project file and commonly used assets
            if (_project != null)
            {
                PrefetchAsset(_project.ID, 10);
            }

            // Prefetch small assets that are likely to be used soon
            foreach (var metadata in _metadataById.Values.Where(m => m.FileSize < 1024 * 1024))
            {
                PrefetchAsset(metadata.ID, 5);
            }
        }

        private async Task ProcessMetaFileAsync(string metaFilePath)
        {
            try
            {
                var fileInfo = new FileInfo(metaFilePath);

                // Read the exact file size instead of fixed buffer
                byte[] fileBytes = await File.ReadAllBytesAsync(metaFilePath);

                // Use the exact file content for deserialization
                var metadata = AssetMetadata.FromBytes(fileBytes);

                // Update file size information
                var assetPath = GetFullPath(metadata.Path);
                if (File.Exists(assetPath))
                {
                    metadata.FileSize = new FileInfo(assetPath).Length;
                }

                var normalizedPath = NormalizePath(metadata.Path.ToString());
                _metadataByPath[normalizedPath] = metadata;
                _metadataById[metadata.ID] = metadata;

                _logger.Debug($"Processed meta file: {metaFilePath}");
            }
            catch (EndOfStreamException ex)
            {
                _logger.Warn(ex, $"Meta file is shorter than expected: {metaFilePath}");
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
                using var stream = new FileStream(assetFilePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan);
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
            {
                Directory.CreateDirectory(directory);
            }

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
            {
                return existing;
            }

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

                // Use pooled arrays for mesh data
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
                // Check cache first
                if (_assetCache.TryGetValue<IAsset<T>>(path.ToString(), out var cachedAsset))
                {
                    return cachedAsset;
                }

                var fileInfo = new FileInfo(fullPath);
                IAsset asset;

                // Use memory mapping for large files
                if (fileInfo.Length > 1024 * 1024) // 1MB threshold
                {
                    asset = await LoadAssetWithMemoryMapping(fullPath);
                }
                else
                {
                    // Use optimized file streaming for small files
                    using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);
                    asset = await _serializer.DeserializeMetadataAsync(stream);
                    stream.Position = 0;
                    await LoadDataForAssetAsync(asset, stream);
                }

                RegisterAsset(asset);

                // Add to cache with size-based expiration
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetSize(((IAsset<T>)asset).GetData()?.GetHashCode() ?? 0)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30));

                _assetCache.Set(path.ToString(), asset, cacheEntryOptions);

                return (IAsset<T>)asset;
            }
            finally
            {
                assetLock.Release();
            }
        }

        private async Task<IAsset> LoadAssetWithMemoryMapping(string fullPath)
        {
            var fileInfo = new FileInfo(fullPath);
            using var mmf = MemoryMappedFile.CreateFromFile(fullPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, fileInfo.Length, MemoryMappedFileAccess.Read);

            var asset = await _serializer.DeserializeMetadataAsync(stream);
            stream.Position = 0;
            await LoadDataForAssetAsync(asset, stream);
            return asset;
        }

        private async Task<IAsset> LoadAssetWithOptimizationsAsync(AssetMetadata metadata)
        {
            var fullPath = GetFullPath(metadata.Path);

            // Use memory mapping for large assets
            if (metadata.FileSize > 1024 * 1024)
            {
                return await LoadAssetWithMemoryMapping(fullPath);
            }
            else
            {
                // Use pooled memory stream for small assets
                var memoryStream = _memoryStreamPool.Get();
                try
                {
                    using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan);

                    await fileStream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    var asset = await _serializer.DeserializeMetadataAsync(memoryStream);
                    memoryStream.Position = 0;
                    await LoadDataForAssetAsync(asset, memoryStream);

                    return asset;
                }
                finally
                {
                    memoryStream.SetLength(0); // Reset for reuse
                    _memoryStreamPool.Return(memoryStream);
                }
            }
        }

        public async Task SaveAsync(IAsset asset)
        {
            asset.BeforeSaving();
            await SaveAsync(asset, GetFullPath(asset.Path));
            asset.AfterSaving();
            await SaveMetadataAsync(asset);

            // Update cache
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSize(asset.GetData()?.GetHashCode() ?? 0)
                .SetSlidingExpiration(TimeSpan.FromMinutes(30));

            _assetCache.Set(asset.Path.ToString(), asset, cacheEntryOptions);
        }

        private async Task SaveAsync(IAsset asset, string fullPath)
        {
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
            await assetLock.WaitAsync();

            try
            {
                asset.UpdateModified();
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Use optimized file writing with write-through caching
                using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, OptimalBufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous);
                await _serializer.SerializeAsync(asset, stream);
            }
            finally
            {
                assetLock.Release();
            }
        }

        private async Task LoadDataForAssetAsync(IAsset asset, Stream stream)
        {
            if (asset.IsDataLoaded)
            {
                return;
            }

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

            // Add to cache
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSize(asset.GetData()?.GetHashCode() ?? 0)
                .SetSlidingExpiration(TimeSpan.FromMinutes(30));

            _assetCache.Set(asset.ID.ToString(), asset, cacheEntryOptions);
            _assetCache.Set(pathKey, asset, cacheEntryOptions);

            OnAssetRegistered?.Invoke(asset);
            OnAssetsChanged?.Invoke();
        }

        public bool TryGetAsset<T>(Guid assetID, out T asset) where T : class, IAsset
        {
            // Check cache first
            if (_assetCache.TryGetValue<T>(assetID.ToString(), out var cachedAsset))
            {
                asset = cachedAsset;
                return true;
            }

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

            // Check cache first
            if (_assetCache.TryGetValue<T>(normalizedPath, out var cachedAsset))
            {
                asset = cachedAsset;
                return true;
            }

            if (_assetsByPath.TryGetValue(normalizedPath, out var foundAsset) && foundAsset is T typedAsset)
            {
                asset = typedAsset;
                return true;
            }

            asset = null!;
            return false;
        }

        public async Task<T?> GetAssetAsync<T>(Guid assetId) where T : class, IAsset
        {
            if (TryGetAsset<T>(assetId, out var asset))
            {
                return asset;
            }

            if (!_metadataById.TryGetValue(assetId, out var metadata))
            {
                throw new FileNotFoundException($"Asset not found: {assetId}");
            }

            var lazyTask = _lazyAssets.GetOrAdd(assetId, id => new Lazy<Task<IAsset>>(
                () => LoadAssetWithOptimizationsAsync(metadata),
                LazyThreadSafetyMode.ExecutionAndPublication));

            var result = await lazyTask.Value;
            return result as T;
        }

        private string GetFullPath(AssetPath assetPath)
        {
            if (Path.IsPathRooted(assetPath.ToString()))
            {
                return assetPath.ToString();
            }

            return Path.Combine(_basePath, assetPath.ToString());
        }

        private string GetMetaPath(string assetPath) => assetPath + MetaExtension;

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        public void Dispose()
        {
            _prefetchCancellation.Cancel();
            _prefetchWorker?.Wait(5000);

            foreach (var lockObj in _assetLocks.Values)
            {
                lockObj.Dispose();
            }

            foreach (var (mmf, accessor) in _mappedFiles.Values)
            {
                accessor.Dispose();
                mmf.Dispose();
            }

            _assetCache.Dispose();
            _fileWatcher?.Dispose();
            _prefetchCancellation.Dispose();
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
            return GetAsset(assetID) as T;
        }
        public IAsset? GetAsset(Guid assetID)
        {
            return _assetsById.TryGetValue(assetID, out var asset) ? asset: null;
        }

        public async Task<T?> LoadAssetByIdAsync<T>(Guid assetId, CancellationToken ct = default) where T : class, IAsset
        {
            if (TryGetAsset(assetId, out T? asset))
            {
                return asset;
            }

            if (!_metadataById.TryGetValue(assetId, out var metadata))
            {
                _logger.Warn($"Metadata not found for asset ID: {assetId}");
                return null;
            }

            try
            {
                return (T?)await LoadAssetWithOptimizationsAsync(metadata);
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
            {
                return false;
            }

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
            {
                return true;
            }

            if (_metadataByPath.ContainsKey(newNormalizedPath))
            {
                return false;
            }

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

                // Update cache
                _assetCache.Remove(oldNormalizedPath);
                _assetCache.Remove(metadata.ID.ToString());

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetSize(loadedAsset?.GetData()?.GetHashCode() ?? 0)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30));

                _assetCache.Set(newNormalizedPath, loadedAsset, cacheEntryOptions);
                _assetCache.Set(metadata.ID.ToString(), loadedAsset, cacheEntryOptions);

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
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(metaPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var metadataBytes = metadata.ToBytes();
            await stream.WriteAsync(metadataBytes);
        }

        public async Task<bool> MoveAssetAsync(string currentAssetPath, AssetPath newPath)
        {
            var normalizedPath = NormalizePath(currentAssetPath);
            if (!_metadataByPath.TryGetValue(normalizedPath, out var metadata))
            {
                return false;
            }

            return await MoveAssetInternalAsync(metadata, newPath);
        }

        public async Task<bool> MoveAssetAsync(Guid assetId, AssetPath newPath)
        {
            if (!_metadataById.TryGetValue(assetId, out var metadata))
            {
                return false;
            }

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
                _lazyAssets.TryRemove(metadata.ID, out _);

                // Remove from cache
                _assetCache.Remove(metadata.ID.ToString());
                _assetCache.Remove(NormalizePath(metadata.Path.ToString()));

                if (removeFile)
                {
                    var fullPath = GetFullPath(metadata.Path);
                    var metaPath = GetMetaPath(fullPath);

                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }

                    if (File.Exists(metaPath))
                    {
                        File.Delete(metaPath);
                    }
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
            _assetCache.Clear();
            _lazyAssets.Clear();
            _logger.Info("Asset cache cleared");
        }

        public void RemoveFromCache(Guid assetId)
        {
            if (_metadataById.TryGetValue(assetId, out var metadata))
            {
                _assetsById.TryRemove(assetId, out _);
                _assetsByPath.TryRemove(NormalizePath(metadata.Path.ToString()), out _);
                _assetCache.Remove(assetId.ToString());
                _assetCache.Remove(NormalizePath(metadata.Path.ToString()));
                _lazyAssets.TryRemove(assetId, out _);
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

    // Supporting classes
    public record struct AssetLoadRequest(Guid AssetId);

    // Object pool policies
    public class MemoryStreamPooledObjectPolicy : IPooledObjectPolicy<MemoryStream>
    {
        public MemoryStream Create()
        {
            return new MemoryStream();
        }

        public bool Return(MemoryStream obj)
        {
            obj.SetLength(0);
            return true;
        }
    }

    public class BufferPooledObjectPolicy : IPooledObjectPolicy<byte[]>
    {
        private readonly int _bufferSize;

        public BufferPooledObjectPolicy(int bufferSize)
        {
            _bufferSize = bufferSize;
        }

        public byte[] Create()
        {
            return new byte[_bufferSize];
        }

        public bool Return(byte[] obj)
        {
            Array.Clear(obj, 0, obj.Length);
            return true;
        }
    }
}