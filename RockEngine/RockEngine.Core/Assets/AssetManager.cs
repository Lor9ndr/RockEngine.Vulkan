using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.ObjectPool;

using NLog;

using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Assets.RockEngine.Core.Assets;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;

using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace RockEngine.Core.Assets
{
    // Strategy Pattern for asset loading with separate metadata/data
    public interface IAssetLoadStrategy
    {
        bool CanHandle(long fileSize);
        Task<AssetMetadata> LoadMetadataAsync(string filePath, IAssetSerializer serializer);
        Task<IAsset> LoadAssetAsync(string filePath, AssetMetadata assetMetadata, IAssetSerializer serializer);
        Task LoadDataAsync<T>(IAsset<T> asset, string filePath, IAssetSerializer serializer) where T : class;
        Task LoadDataAsync(IAsset asset, Type dataType, string filePath, IAssetSerializer serializer);
    }

    public class MemoryMappedLoadStrategy : IAssetLoadStrategy
    {
        public bool CanHandle(long fileSize) => fileSize > 1024 * 1024;

        public async Task<AssetMetadata> LoadMetadataAsync(string filePath, IAssetSerializer serializer)
        {
            var fileInfo = new FileInfo(filePath);
            using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, fileInfo.Length, MemoryMappedFileAccess.Read);

            return await serializer.DeserializeMetadataAsync(stream);
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
            var fileInfo = new FileInfo(filePath);
            using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, fileInfo.Length, MemoryMappedFileAccess.Read);

            await LoadDataForAssetAsync(asset, dataType, stream, serializer);
        }

        public async Task<IAsset> LoadAssetAsync(string filePath, AssetMetadata assetMetadata, IAssetSerializer serializer)
        {
            var fileInfo = new FileInfo(filePath);
            using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, fileInfo.Length, MemoryMappedFileAccess.Read);
            return (IAsset)await serializer.DeserializeAssetAsync(stream, assetMetadata.AssetType);
        }
    }

    public class StreamLoadStrategy : IAssetLoadStrategy
    {
        private readonly ObjectPool<MemoryStream> _memoryStreamPool;
        private const int OptimalBufferSize = 65536;

        public StreamLoadStrategy(ObjectPool<MemoryStream> memoryStreamPool)
        {
            _memoryStreamPool = memoryStreamPool;
        }

        public bool CanHandle(long fileSize) => true; // Fallback strategy

        public async Task<AssetMetadata> LoadMetadataAsync(string filePath, IAssetSerializer serializer)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan);

            return await serializer.DeserializeMetadataAsync(fileStream);
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
            var memoryStream = _memoryStreamPool.Get();
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan);

                await fileStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                await LoadDataForAssetAsync(asset, dataType, memoryStream, serializer);
            }
            finally
            {
                memoryStream.SetLength(0);
                _memoryStreamPool.Return(memoryStream);
            }
        }

        public async Task<IAsset> LoadAssetAsync(string filePath, AssetMetadata assetMetadata, IAssetSerializer serializer)
        {
            var memoryStream = _memoryStreamPool.Get();
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, OptimalBufferSize, FileOptions.SequentialScan);

                await fileStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return (IAsset)await serializer.DeserializeAssetAsync(memoryStream, assetMetadata.AssetType);

            }
            finally
            {
                memoryStream.SetLength(0);
                _memoryStreamPool.Return(memoryStream);
            }
        }
    }

    // Repository Pattern for asset storage
    public interface IAssetRepository
    {
        void Add(IAsset asset);
        bool TryGet(Guid id, out IAsset asset);
        bool TryGet(string path, out IAsset asset);
        void Remove(Guid id);
        void Remove(string path);
        IEnumerable<IAsset> GetAll();
        void Clear();
    }

    public class AssetRepository : IAssetRepository
    {
        private readonly ConcurrentDictionary<Guid, IAsset> _assetsById = new();
        private readonly ConcurrentDictionary<string, IAsset> _assetsByPath = new(StringComparer.OrdinalIgnoreCase);

        public void Add(IAsset asset)
        {
            var pathKey = AssetPathNormalizer.Normalize(asset.Path.ToString());
            if (_assetsById.ContainsKey(asset.ID))
            {
                throw new InvalidOperationException("Same assetID already added");
            }
            _assetsById[asset.ID] = asset;
            if (_assetsByPath.ContainsKey(pathKey))
            {
                throw new InvalidOperationException("Same asset path already added");
            }
            _assetsByPath[pathKey] = asset;
        }

        public bool TryGet(Guid id, out IAsset asset) => _assetsById.TryGetValue(id, out asset);
        public bool TryGet(string path, out IAsset asset) => _assetsByPath.TryGetValue(AssetPathNormalizer.Normalize(path), out asset);

        public void Remove(Guid id)
        {
            if (_assetsById.TryRemove(id, out var asset))
            {
                _assetsByPath.TryRemove(AssetPathNormalizer.Normalize(asset.Path.ToString()), out _);
            }
        }

        public void Remove(string path)
        {
            var normalizedPath = AssetPathNormalizer.Normalize(path);
            if (_assetsByPath.TryRemove(normalizedPath, out var asset))
            {
                _assetsById.TryRemove(asset.ID, out _);
            }
        }

        public IEnumerable<IAsset> GetAll() => _assetsById.Values;

        public void Clear()
        {
            _assetsById.Clear();
            _assetsByPath.Clear();
        }
    }

    // Factory Pattern for asset creation
    public interface IAssetFactory
    {
        T Create<T>(AssetPath path, string? name = null) where T : IAsset;
        Task<ModelAsset> CreateModelFromFileAsync(string filePath, string? modelName = null, string parentPath = "Models");
        MaterialAsset CreateMaterial(string name, string template, List<AssetReference<TextureAsset>>? textures = null, Dictionary<string, object>? parameters = null);
    }

    public class AssetFactory : IAssetFactory
    {
        private readonly AssimpLoader _assimpLoader;
        private readonly IAssetRepository _assetRepository;

        public AssetFactory(AssimpLoader assimpLoader, IAssetRepository assetRepository)
        {
            _assimpLoader = assimpLoader;
            _assetRepository = assetRepository;
        }

        public T Create<T>(AssetPath path, string? name = null) where T : IAsset
        {
            var asset = (T)IoC.Container.GetInstance(typeof(T));
            asset.Path = path;
            asset.Name = name ?? Path.GetFileNameWithoutExtension(path.FullPath);
            return asset;
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

        public async Task<ModelAsset> CreateModelFromFileAsync(string filePath, string? modelName = null, string parentPath = "Models")
        {
            modelName ??= Path.GetFileNameWithoutExtension(filePath);
            var meshesData = await _assimpLoader.LoadMeshesAsync(filePath);
            var modelAsset = Create<ModelAsset>(new AssetPath(parentPath, modelName));

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

                var textureIDs = await CreateTexturesAsync(meshData.Textures, $"{parentPath}/{modelName}/Textures", textureCache);

                materialAsset.SetData(new MaterialData
                {
                    PipelineName = "Geometry",
                    Textures = textureIDs.Select(id => new AssetReference<TextureAsset>(id)).ToList()
                });

                modelAsset.AddPart(new ModelPart { Mesh = meshAsset, Material = materialAsset });
                _assetRepository.Add(meshAsset);
                _assetRepository.Add(materialAsset);
            }

            _assetRepository.Add(modelAsset);
            return modelAsset;
        }

        private async Task<List<Guid>> CreateTexturesAsync(List<string> texturePaths, string textureFolder, Dictionary<string, TextureAsset> textureCache)
        {
            var textureIDs = new List<Guid>();

            foreach (var texturePath in texturePaths)
            {
                if (!textureCache.TryGetValue(texturePath, out var textureAsset))
                {
                    var textureName = Path.GetFileName(texturePath);
                    textureAsset = Create<TextureAsset>(new AssetPath(textureFolder, textureName));
                    textureAsset.SetData(new TextureData
                    {
                        FilePaths = [texturePath],
                        GenerateMipmaps = true,
                        Type = TextureType.Texture2D
                    });
                    textureCache[texturePath] = textureAsset;
                    _assetRepository.Add(textureAsset);
                }
                textureIDs.Add(textureAsset.ID);
            }

            return textureIDs;
        }
    }

    // Metadata management
    public interface IMetadataManager
    {
        Task<AssetMetadata> GetOrCreateMetadataAsync(string assetFilePath);
        Task SaveMetadataAsync(IAsset asset);
        bool TryGetMetadata(Guid id, out AssetMetadata metadata);
        bool TryGetMetadata(string path, out AssetMetadata metadata);
        AssetMetadata? GetMetadata(Guid id);
        AssetMetadata? GetMetadata(string path);
        void Clear();
    }

    public class MetadataManager : IMetadataManager
    {
        private readonly ConcurrentDictionary<Guid, AssetMetadata> _metadataById = new();
        private readonly ConcurrentDictionary<string, AssetMetadata> _metadataByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly IAssetSerializer _serializer;
        private string _basePath;

        public MetadataManager(IAssetSerializer serializer)
        {
            _serializer = serializer;
        }

        public void SetBasePath(string basePath)
        {
            _basePath = basePath;
            Clear(); // Clear metadata when base path changes
        }

        public async Task<AssetMetadata> GetOrCreateMetadataAsync(string assetFilePath)
        {
            var metaFilePath = GetMetaPath(assetFilePath);

           AssetMetadata assetMetadata;
            if (File.Exists(metaFilePath))
            {
                assetMetadata =  await LoadMetadataFromFileAsync(metaFilePath);
            }
            else
            {
                assetMetadata = await CreateMetadataFromAssetAsync(assetFilePath);
            }
            _metadataByPath[assetFilePath] = assetMetadata;
            _metadataById[assetMetadata.ID] = assetMetadata;
            return assetMetadata;
        }

        public async Task SaveMetadataAsync(IAsset asset)
        {
            var metadata = new AssetMetadata(asset);
            await SaveMetadataToDiskAsync(metadata);

            var normalizedPath = AssetPathNormalizer.Normalize(asset.Path.ToString());
            _metadataByPath[normalizedPath] = metadata;
            _metadataById[asset.ID] = metadata;
        }

        public bool TryGetMetadata(Guid id, out AssetMetadata metadata) => _metadataById.TryGetValue(id, out metadata);
        public bool TryGetMetadata(string path, out AssetMetadata metadata) => _metadataByPath.TryGetValue(AssetPathNormalizer.Normalize(path), out metadata);

        public AssetMetadata? GetMetadata(Guid id) => _metadataById.TryGetValue(id, out var metadata) ? metadata : null;
        public AssetMetadata? GetMetadata(string path) => _metadataByPath.TryGetValue(AssetPathNormalizer.Normalize(path), out var metadata) ? metadata : null;

        public void Clear()
        {
            _metadataById.Clear();
            _metadataByPath.Clear();
        }

        private async Task<AssetMetadata> LoadMetadataFromFileAsync(string metaFilePath)
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(metaFilePath);
            return AssetMetadata.FromBytes(fileBytes);
        }

        private async Task<AssetMetadata> CreateMetadataFromAssetAsync(string assetFilePath)
        {
            using var stream = new FileStream(assetFilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, AssetConstants.OptimalBufferSize, FileOptions.SequentialScan);
            var asset = await _serializer.DeserializeMetadataAsync(stream);
            return asset;
        }

        private async Task SaveMetadataToDiskAsync(AssetMetadata metadata)
        {
            var metaPath = GetMetaPath(GetFullPath(metadata.Path));
            var directory = Path.GetDirectoryName(metaPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(metaPath, metadata.ToBytes());
        }

        private string GetMetaPath(string assetPath) => assetPath + AssetConstants.MetaExtension;
        private string GetFullPath(AssetPath path) => Path.Combine(_basePath, path.ToString());
    }

    // Asset loading service
    public interface IAssetLoader
    {
        Task<IAsset> LoadAssetAsync(Guid assetId);
        Task<IAsset> LoadAssetAsync(string assetPath);
        Task LoadAssetDataAsync<T>(IAsset<T> asset) where T : class;
        Task LoadAssetDataAsync(IAsset asset, Type dataType);
        Task<T> LoadAssetAsync<T>(Guid assetId) where T : class, IAsset;
        Task<T> LoadAssetAsync<T>(string assetPath) where T : class, IAsset;
        void SetBasePath(string basePath);
    }

    public class AssetLoader : IAssetLoader
    {
        private readonly IMetadataManager _metadataManager;
        private readonly IAssetRepository _assetRepository;
        private readonly IAssetSerializer _serializer;
        private readonly IAssetLoadStrategy[] _loadStrategies;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _assetLocks;
        private string _basePath;

        public AssetLoader(IMetadataManager metadataManager, IAssetRepository assetRepository,
                         IAssetSerializer serializer, IAssetLoadStrategy[] loadStrategies,
                         ConcurrentDictionary<string, SemaphoreSlim> assetLocks)
        {
            _metadataManager = metadataManager;
            _assetRepository = assetRepository;
            _serializer = serializer;
            _loadStrategies = loadStrategies;
            _assetLocks = assetLocks;
        }

        public void SetBasePath(string basePath)
        {
            _basePath = basePath;
            if (_metadataManager is MetadataManager mm)
            {
                mm.SetBasePath(basePath);
            }
        }

        public async Task<IAsset> LoadAssetAsync(Guid assetId)
        {
            if (_assetRepository.TryGet(assetId, out var cachedAsset))
            {
                return cachedAsset;
            }

            var metadata = _metadataManager.GetMetadata(assetId) ?? throw new FileNotFoundException($"Asset metadata not found: {assetId}");
            return await LoadAssetInternalAsync(metadata);
        }

        public async Task<IAsset> LoadAssetAsync(string assetPath)
        {
            var normalizedPath = AssetPathNormalizer.Normalize(assetPath);
            if (_assetRepository.TryGet(normalizedPath, out var cachedAsset))
            {
                return cachedAsset;
            }

            var metadata = await _metadataManager.GetOrCreateMetadataAsync(normalizedPath);
            return metadata == null
                ? throw new FileNotFoundException($"Asset metadata not found: {assetPath}")
                : await LoadAssetInternalAsync(metadata);
        }

        public async Task<T> LoadAssetAsync<T>(Guid assetId) where T : class, IAsset
        {
            var asset = await LoadAssetAsync(assetId);
            return asset as T ?? throw new InvalidCastException($"Asset {assetId} is not of type {typeof(T).Name}");
        }

        public async Task<T> LoadAssetAsync<T>(string assetPath) where T : class, IAsset
        {
            var asset = await LoadAssetAsync(assetPath);
            return asset as T ?? throw new InvalidCastException($"Asset {assetPath} is not of type {typeof(T).Name}");
        }

        public async Task LoadAssetDataAsync<T>(IAsset<T> asset) where T : class
        {
            await LoadAssetDataAsync(asset, typeof(T));
        }
        public async Task LoadAssetDataAsync(IAsset asset, Type dataType)
        {
            if (asset.IsDataLoaded) return;

            var fullPath = GetFullPath(asset.Path);
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));

            await assetLock.WaitAsync();
            try
            {
                if (!asset.IsDataLoaded)
                {
                    var strategy = GetLoadStrategy(new FileInfo(fullPath).Length);
                    await strategy.LoadDataAsync(asset, dataType, fullPath, _serializer);
                }
            }
            finally
            {
                assetLock.Release();
            }
        }

        private async Task<IAsset> LoadAssetInternalAsync(AssetMetadata metadata)
        {
            
            var fullPath = GetFullPath(metadata.Path);
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));

            await assetLock.WaitAsync();
            try
            {
                // Check cache again after acquiring lock
                if (_assetRepository.TryGet(metadata.ID, out var cachedAsset))
                {
                    return cachedAsset;
                }

                var strategy = GetLoadStrategy(metadata.FileSize);
                var asset = await strategy.LoadAssetAsync(fullPath, metadata, _serializer);
                _assetRepository.Add(asset);
                return asset;
            }
            finally
            {
                assetLock.Release();
            }
        }

        private IAssetLoadStrategy GetLoadStrategy(long fileSize)
        {
            return _loadStrategies.FirstOrDefault(s => s.CanHandle(fileSize))
                ?? _loadStrategies.Last(); // Use last as fallback
        }

        private string GetFullPath(AssetPath path)
        {
            if (!string.IsNullOrEmpty(_basePath))
            {
                var combinedPath = Path.Combine(_basePath, path.ToString());
                if (File.Exists(combinedPath))
                {
                    return combinedPath;
                }
            }
           
            if(File.Exists(path.FullPath))
                return path.FullPath;
            throw new InvalidOperationException($"Failed to find asset by path {path}");
        }
    }

    // Utility classes
    public static class AssetPathNormalizer
    {
        public static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }

    public static class AssetConstants
    {
        public const string ProjectExtension = ".rockproj";
        public const string AssetExtension = ".asset";
        public const string MetaExtension = ".meta";
        public const int MetaFileBufferSize = 8192;
        public const int OptimalBufferSize = 65536;
        public const long MaxCacheSize = 1024 * 1024 * 128;
    }

    // Project management
    public interface IProjectManager
    {
        ProjectAsset? CurrentProject { get; }
        Task<ProjectAsset> CreateProjectAsync(string projectPath, string projectName);
        Task<ProjectAsset> LoadProjectAsync(string projectFilePath);
        void UnloadProject();
        bool IsProjectLoaded { get; }
    }

    // Main AssetManager class (refactored)
    public sealed class AssetManager : IDisposable, IProjectManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly MemoryCache _assetCache = new(new MemoryCacheOptions
        {
            SizeLimit = AssetConstants.MaxCacheSize,
            CompactionPercentage = 0.25
        });

        private readonly ObjectPool<MemoryStream> _memoryStreamPool;
        private readonly ObjectPool<byte[]> _bufferPool;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _assetLocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Guid, Lazy<Task<IAsset>>> _lazyAssets = new();

        private readonly IAssetSerializer _serializer;
        private readonly IAssetRepository _assetRepository;
        private readonly IAssetFactory _assetFactory;
        private readonly IMetadataManager _metadataManager;
        private readonly IAssetLoader _assetLoader;

        private ProjectAsset? _currentProject;
        private FileSystemWatcher? _fileWatcher;

        public ProjectAsset? CurrentProject => _currentProject;
        public bool IsProjectLoaded => _currentProject != null;

        public string BasePath => _currentProject?.Path.RelativePath ?? string.Empty;
        public event Action<Guid>? OnAssetRemoved;
        public event Action<IAsset>? OnAssetRegistered;
        public event Action? OnAssetsChanged;
        public event Action<ProjectAsset>? OnProjectLoaded;
        public event Action? OnProjectUnloaded;

        public AssetManager(IAssetSerializer serializer, AssimpLoader assimpLoader)
        {
            _logger.Info("AssetManager initializing...");
            _serializer = serializer;

            // Initialize object pools
            _memoryStreamPool = CreateMemoryStreamPool();
            _bufferPool = CreateBufferPool();

            // Initialize repository and factory
            _assetRepository = new AssetRepository();
            _assetFactory = new AssetFactory(assimpLoader, _assetRepository);

            // Initialize metadata manager and loader (base path will be set when project is loaded)
            _metadataManager = new MetadataManager(serializer);

            var loadStrategies = new IAssetLoadStrategy[]
            {
                new MemoryMappedLoadStrategy(),
                new StreamLoadStrategy(_memoryStreamPool)
            };

            _assetLoader = new AssetLoader(_metadataManager, _assetRepository, _serializer,
                                         loadStrategies, _assetLocks);
        }

        private static ObjectPool<MemoryStream> CreateMemoryStreamPool() =>
            new DefaultObjectPool<MemoryStream>(
                new MemoryStreamPooledObjectPolicy(),
                Environment.ProcessorCount);

        private static ObjectPool<byte[]> CreateBufferPool() =>
            new DefaultObjectPool<byte[]>(
                new BufferPooledObjectPolicy(AssetConstants.OptimalBufferSize),
                Environment.ProcessorCount * 4);

        // Project management
        public async Task<ProjectAsset> CreateProjectAsync(string projectPath, string projectName)
        {
            if (IsProjectLoaded)
            {
                UnloadProject();
            }

            var projectDir = Path.Combine(projectPath, projectName);
            if (!Directory.Exists(projectDir))
            {
                Directory.CreateDirectory(projectDir);
            }

            var projectAssetPath = new AssetPath(projectDir, projectName, AssetConstants.ProjectExtension);
            var project = _assetFactory.Create<ProjectAsset>(projectAssetPath, projectName);

            await SetCurrentProjectAsync(project);
            await SaveAsync(project); // Save the project file

            return project;
        }

        public async Task<ProjectAsset> LoadProjectAsync(string projectFilePath)
        {
            if (IsProjectLoaded)
            {
                UnloadProject();
            }

            if (!File.Exists(projectFilePath))
            {
                throw new FileNotFoundException($"Project file not found: {projectFilePath}");
            }

            // Load project asset
            var project = await _assetLoader.LoadAssetAsync<ProjectAsset>(projectFilePath);
            await SetCurrentProjectAsync(project);

            return project;
        }

        public void UnloadProject()
        {
            if (!IsProjectLoaded) return;

            _logger.Info("Unloading project: {ProjectName}", _currentProject!.Name);

            // Clear all asset references
            _assetRepository.Clear();
            _metadataManager.Clear();
            _assetCache.Compact(100); // Clear entire cache
            _lazyAssets.Clear();

            // Dispose and clear asset locks
            foreach (var lockObj in _assetLocks.Values)
            {
                lockObj.Dispose();
            }
            _assetLocks.Clear();

            _fileWatcher?.Dispose();
            _fileWatcher = null;

            var oldProject = _currentProject;
            _currentProject = null;

            OnProjectUnloaded?.Invoke();
            _logger.Info("Project unloaded: {ProjectName}", oldProject!.Name);
        }

        private async Task SetCurrentProjectAsync(ProjectAsset project)
        {
            _currentProject = project;
            _assetLoader.SetBasePath(BasePath);

            // Initialize file watcher for project directory
            InitializeFileWatcher();

            // Register project asset
            RegisterAsset(project);

            _logger.Info("Project loaded: {ProjectName} at {BasePath}", project.Name, BasePath);
            OnProjectLoaded?.Invoke(project);
            var files = Directory.EnumerateFiles(BasePath, "*.asset", SearchOption.AllDirectories);
            foreach (var item in files)
            {
                await _metadataManager.GetOrCreateMetadataAsync(item);
            }
        }

        private void InitializeFileWatcher()
        {
            if (string.IsNullOrEmpty(BasePath) || !Directory.Exists(BasePath)) return;

            _fileWatcher = new FileSystemWatcher(BasePath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Created += OnFileCreated;
            _fileWatcher.Deleted += OnFileDeleted;
            _fileWatcher.Renamed += OnFileRenamed;

            _fileWatcher.EnableRaisingEvents = true;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Handle file changes
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            // Handle file creation
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            // Handle file deletion
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            // Handle file renaming
        }

        // Core asset loading methods with project validation
        public async Task<T?> GetAssetAsync<T>(Guid assetId) where T : class, IAsset
        {
            EnsureProjectLoaded();
            if (_assetRepository.TryGet(assetId, out var asset) && asset is T typedAsset)
            {
                if (!typedAsset.IsDataLoaded)
                {
                    await _assetLoader.LoadAssetDataAsync(typedAsset, typedAsset.GetDataType());
                }
                return typedAsset;
            }

            return await _assetLoader.LoadAssetAsync<T>(assetId);
        }

        public async Task<T?> LoadAssetAsync<T>(string assetPath) where T : class, IAsset
        {
            EnsureProjectLoaded();
            return await _assetLoader.LoadAssetAsync<T>(assetPath);
        }

        public async Task LoadAssetDataAsync(IAsset asset)
        {
            EnsureProjectLoaded();
            await _assetLoader.LoadAssetDataAsync(asset, asset.GetDataType());
        }

        public async Task<IAsset<T>> LoadAsync<T>(AssetPath path) where T : class
        {
            EnsureProjectLoaded();
            var pathKey = AssetPathNormalizer.Normalize(path.ToString());

            if (_assetRepository.TryGet(pathKey, out var existingAsset) && existingAsset is IAsset<T> typedAsset)
            {
                if (!typedAsset.IsDataLoaded)
                {
                    await _assetLoader.LoadAssetDataAsync(typedAsset);
                }
                return typedAsset;
            }

            return await _assetLoader.LoadAssetAsync<IAsset<T>>(pathKey);
        }

        public async Task SaveAsync(IAsset asset)
        {
            EnsureProjectLoaded();
            asset.BeforeSaving();
            if (asset is MeshAsset mesh)
            {

            }
            await SaveAssetToDiskAsync(asset);
            
            asset.AfterSaving();
            await _metadataManager.SaveMetadataAsync(asset);

            UpdateCache(asset);
        }

        private async Task SaveAssetToDiskAsync(IAsset asset)
        {
            if (!asset.Path.IsValid)
            {
                _logger.Warn("Failed to save asset, because of it's path:{asset.ID}", asset.ID);
                //return;
            }

            var fullPath = GetFullPath(asset.Path);
            var assetLock = _assetLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));

            await assetLock.WaitAsync();
            try
            {
                asset.UpdateModified();
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, AssetConstants.OptimalBufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous);
                await _serializer.SerializeAsync(asset, stream);
            }
            finally
            {
                assetLock.Release();
            }
        }

        private void UpdateCache(IAsset asset)
        {
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSize(asset.GetData()?.GetHashCode() ?? 0)
                .SetSlidingExpiration(TimeSpan.FromMinutes(30));

            _assetCache.Set(asset.Path.ToString(), asset, cacheEntryOptions);
            _assetCache.Set(asset.ID.ToString(), asset, cacheEntryOptions);
        }

        public void RegisterAsset(IAsset asset)
        {
            EnsureProjectLoaded();
            try
            {
                _assetRepository.Add(asset);
            }
            catch (InvalidOperationException _)
            {

            }
            UpdateCache(asset);
            OnAssetRegistered?.Invoke(asset);
            OnAssetsChanged?.Invoke();
        }

        public T Create<T>(AssetPath path, string? name = null) where T : IAsset
        {
            EnsureProjectLoaded();
            if (typeof(T) == typeof(ProjectAsset))
            {
                throw new InvalidOperationException($"For project creation use {nameof(CreateProjectAsync)} method");
            }
            return _assetFactory.Create<T>(path, name);
        }

        public async Task<ModelAsset> LoadModelAsync(string filePath, string? modelName = null, string parentPath = "Models")
        {
            EnsureProjectLoaded();
            var model = await _assetFactory.CreateModelFromFileAsync(filePath, modelName, parentPath);

            // Save all created assets
            var saveTasks = new List<Task>();
            foreach (var asset in _assetRepository.GetAll())
            {
                saveTasks.Add(SaveAsync(asset));
            }

            await Task.WhenAll(saveTasks);
            return model;
        }

        private string GetFullPath(AssetPath path)
        {
            EnsureProjectLoaded();
            return Path.Combine(BasePath, path.ToString());
        }

        private void EnsureProjectLoaded()
        {
            if (!IsProjectLoaded)
            {
                throw new InvalidOperationException("No project is currently loaded. Please load or create a project first.");
            }
        }

        public void Dispose()
        {
            UnloadProject();
            _assetCache.Dispose();
            _fileWatcher?.Dispose();

            foreach (var lockObj in _assetLocks.Values)
            {
                lockObj.Dispose();
            }
        }

        // Maintain original methods for compatibility
        public bool TryGetAsset<T>(Guid assetID, out T asset) where T : class, IAsset
        {
            if (!IsProjectLoaded)
            {
                asset = null!;
                return false;
            }

            if (_assetCache.TryGetValue<T>(assetID.ToString(), out var cachedAsset))
            {
                asset = cachedAsset;
                return true;
            }

            if (_assetRepository.TryGet(assetID, out var foundAsset) && foundAsset is T typedAsset)
            {
                asset = typedAsset;
                return true;
            }

            asset = null!;
            return false;
        }

        public bool TryGetAsset<T>(string pathKey, out T asset) where T : class, IAsset
        {
            if (!IsProjectLoaded)
            {
                asset = null!;
                return false;
            }

            var normalizedPath = AssetPathNormalizer.Normalize(pathKey);

            if (_assetCache.TryGetValue<T>(normalizedPath, out var cachedAsset))
            {
                asset = cachedAsset;
                return true;
            }

            if (_assetRepository.TryGet(normalizedPath, out var foundAsset) && foundAsset is T typedAsset)
            {
                asset = typedAsset;
                return true;
            }

            asset = null!;
            return false;
        }

        public T? GetAsset<T>(Guid assetId) where T : class, IAsset
        {
            if (!IsProjectLoaded) return null;

            if (_assetRepository.TryGet(assetId, out var cachedAsset))
            {
                return (T)cachedAsset;
            }
            return null;
        }
    }

    // Supporting classes
    public record struct AssetLoadRequest(Guid AssetId);

    public class MemoryStreamPooledObjectPolicy : IPooledObjectPolicy<MemoryStream>
    {
        public MemoryStream Create() => new MemoryStream();
        public bool Return(MemoryStream obj)
        {
            obj.SetLength(0);
            return true;
        }
    }

    public class BufferPooledObjectPolicy : IPooledObjectPolicy<byte[]>
    {
        private readonly int _bufferSize;
        public BufferPooledObjectPolicy(int bufferSize) => _bufferSize = bufferSize;
        public byte[] Create() => new byte[_bufferSize];
        public bool Return(byte[] obj)
        {
            Array.Clear(obj, 0, obj.Length);
            return true;
        }
    }

    // AssetMetadata class (maintained from original)
    public sealed class AssetMetadata
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
}