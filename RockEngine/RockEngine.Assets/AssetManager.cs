using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NLog;
using RockEngine.Core.Assets;
using System.Collections.Concurrent;

namespace RockEngine.Assets
{
    public sealed class AssetManager : IDisposable, IAssetManager, IProjectManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly AssetManagerOptions _options;
        private readonly IAssetSerializer _serializer;
        private readonly IAssetRepository _repository;
        private readonly IAssetLoader _loader;
        private readonly IAssetFactory _factory;
        private readonly SemaphoreSlim _loadSemaphore;
        private readonly CancellationTokenSource _cts = new();

        private readonly MemoryCache _assetCache;
        private readonly ConcurrentDictionary<string, Task<IAsset>> _loadingTasks = new();
        private readonly ConcurrentDictionary<Guid, HashSet<Guid>> _dependencyGraph = new();
        private readonly ConcurrentDictionary<Guid, string> _idToPathMap = new();

        private IProject? _currentProject;
        private FileSystemWatcher? _fileWatcher;

        public event Action<AssetChangedEventArgs>? OnAssetChanged;
        public event Action<IProject>? OnProjectLoaded;
        public event Action? OnProjectUnloaded;

        public IProject? CurrentProject => _currentProject;
        public bool IsProjectLoaded => _currentProject != null;
        public string BasePath => _currentProject?.Path.Folder ?? string.Empty;

        public AssetManager(
            IOptions<AssetManagerOptions> options,
            IAssetSerializer serializer,
            IAssetRepository assetRepository,
            IAssetFactory assetFactory,
            IAssetLoader assetLoader)
        {
            _options = options.Value;
            _serializer = serializer;
            _repository = assetRepository;
            _factory = assetFactory;
            _loader = assetLoader;
            _loadSemaphore = new SemaphoreSlim(_options.MaxConcurrentLoads);

            _assetCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = _options.MaxCacheSize,
                ExpirationScanFrequency = TimeSpan.FromMinutes(5)
            });
        }

        #region IAssetManager Implementation


        public async Task<T> GetAssetAsync<T>(Guid assetId) where T : class, IAsset
        {
            var asset = await LoadAssetAsync(assetId);
            return asset as T ?? throw new InvalidCastException($"Asset {assetId} is not of type {typeof(T).Name}");
        }

        public async Task<T> LoadAssetAsync<T>(string assetPath) where T : class, IAsset
        {
            var normalizedPath = AssetPathNormalizer.Normalize(assetPath);

            // Check cache first
            if (_assetCache.TryGetValue<T>(normalizedPath, out var cachedAsset))
                return cachedAsset;

            // Check if already loading
            if (_loadingTasks.TryGetValue(normalizedPath, out var loadingTask))
                return (T)await loadingTask;

            // Create loading task
            var task = LoadAssetInternalAsync<T>(normalizedPath);
            _loadingTasks[normalizedPath] = task;

            try
            {
                var asset = await task;
                CacheAsset(asset);
                return (T)asset;
            }
            finally
            {
                _loadingTasks.TryRemove(normalizedPath, out _);
            }
        }

        private async Task<IAsset> LoadAssetInternalAsync<T>(string normalizedPath) where T : class, IAsset
        {
            await _loadSemaphore.WaitAsync();
            try
            {
                // Check repository
                if (_repository.TryGet(normalizedPath, out var existingAsset) && existingAsset is T typedAsset)
                {
                    if (!typedAsset.IsDataLoaded)
                        await _loader.LoadAssetDataAsync(typedAsset, typedAsset.GetDataType());
                   
                    return typedAsset;
                }

                // Load from disk
                var asset = await _loader.LoadAssetAsync<T>(normalizedPath);
                _repository.Add(asset);

                // Update the ID to path map
                _idToPathMap[asset.ID] = normalizedPath;

                return asset;
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }

        public async Task<IAsset> LoadAssetAsync(Guid assetId)
        {
            // Check repository first
            if (_repository.TryGet(assetId, out var cachedAsset))
                return cachedAsset;

            // Get path from our map
            if (!_idToPathMap.TryGetValue(assetId, out var path))
                throw new FileNotFoundException($"Asset with ID {assetId} not found in index");

            return await LoadAssetAsync<IAsset>(path);
        }

        public async Task<IAsset> LoadAssetAsync(string assetPath)
        {
            return await LoadAssetAsync<IAsset>(assetPath);
        }

        public async Task LoadAssetDataAsync(IAsset asset)
        {
            await _loader.LoadAssetDataAsync(asset, asset.GetDataType());
        }

        public async Task SaveAsync(IAsset asset)
        {
            asset.BeforeSaving();
            await SaveAssetToDiskAsync(asset);
            asset.AfterSaving();
            foreach (var item in asset.Dependencies)
            {
                await SaveAsync(item);
            }
            CacheAsset(asset);
        }

        #endregion

        #region IProjectManager Implementation

        public async Task<T> CreateProjectAsync<T,TData>(string projectPath, string projectName) where T : class, IProject, IAsset<TData> where TData :class, new()
        {
            if (IsProjectLoaded)
                UnloadProject();

            var projectDir = Path.Combine(projectPath, projectName);
            if (!Directory.Exists(projectDir))
                Directory.CreateDirectory(projectDir);

            var projectAssetPath = new AssetPath(projectDir, projectName, AssetConstants.ProjectExtension);
            var project = _factory.Create<T>(projectAssetPath, projectName);
            project.SetData(new TData());

            await SetCurrentProjectAsync(project);
            await SaveAsync(project);

            return project;
        }

        public async Task<T> LoadProjectAsync<T>(string projectFilePath) where T : class, IProject
        {
            if (IsProjectLoaded)
                UnloadProject();

            if (!File.Exists(projectFilePath))
                throw new FileNotFoundException($"Project file not found: {projectFilePath}");

            var project = await LoadAssetAsync<T>(projectFilePath);
            await SetCurrentProjectAsync(project);

            return project;
        }

        public void UnloadProject()
        {
            if (!IsProjectLoaded) return;

            _logger.Info("Unloading project: {ProjectName}", _currentProject!.Name);

            _repository.Clear();
            _assetCache.Compact(100);
            _dependencyGraph.Clear();
            _loadingTasks.Clear();
            _idToPathMap.Clear();

            _fileWatcher?.Dispose();
            _fileWatcher = null;

            var oldProject = _currentProject;
            _currentProject = null;

            OnProjectUnloaded?.Invoke();
            _logger.Info("Project unloaded: {ProjectName}", oldProject!.Name);
        }

        #endregion

        #region Helper Methods

        private string GetFullPath(AssetPath path)
        {
            return Path.Combine(BasePath, path.ToString());
        }

        private void CacheAsset(IAsset asset)
        {
            if (!_options.EnableAssetCaching) return;

            var options = new MemoryCacheEntryOptions
            {
                SlidingExpiration = _options.CacheDuration,
                Size = EstimateAssetSize(asset)
            };

            options.RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                _logger.Debug("Asset evicted from cache: {Key}, Reason: {Reason}", key, reason);

                if (reason == EvictionReason.Replaced || reason == EvictionReason.Expired)
                {
                    if (value is IAsset evictedAsset)
                    {
                     //   evictedAsset.UnloadData();
                    }
                }
            });

            _assetCache.Set(asset.Path.ToString(), asset, options);
            _assetCache.Set(asset.ID.ToString(), asset, options);
        }

        private long EstimateAssetSize(IAsset asset)
        {
            var data = asset.GetData();
            if (data == null) return 1;

            try
            {
                using var stream = new MemoryStream();
                _serializer.SerializeAsync(asset, stream).Wait();
                return stream.Length / 1024; // Size in KB for cache
            }
            catch
            {
                return 1;
            }
        }

        private async Task SaveAssetToDiskAsync(IAsset asset)
        {
            var fullPath = GetFullPath(asset.Path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using var stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.Write,
                FileShare.None, AssetConstants.OptimalBufferSize, FileOptions.Asynchronous);

            await _serializer.SerializeAsync(asset, stream);

            // Update our ID to path map
            var relativePath = Path.GetRelativePath(BasePath, fullPath);
            _idToPathMap[asset.ID] = relativePath;
        }

        #endregion

        #region File System Watcher

        private void InitializeFileWatcher()
        {
            if (string.IsNullOrEmpty(BasePath) || !Directory.Exists(BasePath)) return;

            _fileWatcher = new FileSystemWatcher(BasePath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                Filter = "*" + AssetConstants.AssetExtension
            };

            _fileWatcher.Changed += async (sender, e) => await HandleFileChanged(e);
            _fileWatcher.Created += async (sender, e) => await HandleFileCreated(e);
            _fileWatcher.Deleted += async (sender, e) => await HandleFileDeleted(e);
            _fileWatcher.Renamed += async (sender, e) => await HandleFileRenamed(e);

            _fileWatcher.EnableRaisingEvents = true;
        }

        private async Task HandleFileChanged(FileSystemEventArgs e)
        {
            try
            {
                var normalizedPath = AssetPathNormalizer.Normalize(e.FullPath);

                // Debounce rapid changes
                await Task.Delay(100);

                if (_repository.TryGet(normalizedPath, out var asset))
                {
                    await ReloadAssetAsync(asset);
                    OnAssetChanged?.Invoke(new AssetChangedEventArgs
                    {
                        Asset = asset,
                        ChangeType = AssetChangeType.Modified,
                        Path = normalizedPath
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to handle file change for {Path}", e.FullPath);
            }
        }

        private async Task ReloadAssetAsync(IAsset asset)
        {
            try
            {
                // Unload current data
                asset.UnloadData();

                // Reload from disk
                await _loader.LoadAssetDataAsync(asset, asset.GetDataType());

                // Update cache
                CacheAsset(asset);

                _logger.Info("Hot-reloaded asset: {AssetName}", asset.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reload asset {AssetId}", asset.ID);
            }
        }

        private async Task HandleFileCreated(FileSystemEventArgs e)
        {
            try
            {
                var normalizedPath = AssetPathNormalizer.Normalize(e.FullPath);

                // Load the asset header to get its ID
                using var stream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read);
                var header = await _serializer.DeserializeHeaderAsync(stream);

                // Update our ID to path map
                var relativePath = Path.GetRelativePath(BasePath, e.FullPath);
                _idToPathMap[header.AssetId] = relativePath;

                // Load the full asset
                var asset = await _loader.LoadAssetAsync<IAsset>(relativePath);
                _repository.Add(asset);
                CacheAsset(asset);

                OnAssetChanged?.Invoke(new AssetChangedEventArgs
                {
                    Asset = asset,
                    ChangeType = AssetChangeType.Created,
                    Path = normalizedPath
                });
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to handle file creation for {Path}", e.FullPath);
            }
        }

        private async Task HandleFileDeleted(FileSystemEventArgs e)
        {
            try
            {
                var normalizedPath = AssetPathNormalizer.Normalize(e.FullPath);

                if (_repository.TryGet(normalizedPath, out var asset))
                {
                    _repository.Remove(normalizedPath);
                    _assetCache.Remove(normalizedPath);
                    _assetCache.Remove(asset.ID.ToString());

                    // Remove from ID to path map
                    _idToPathMap.TryRemove(asset.ID, out _);

                    OnAssetChanged?.Invoke(new AssetChangedEventArgs
                    {
                        Asset = asset,
                        ChangeType = AssetChangeType.Deleted,
                        Path = normalizedPath
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to handle file deletion for {Path}", e.FullPath);
            }
        }

        private async Task HandleFileRenamed(RenamedEventArgs e)
        {
            try
            {
                var oldPath = AssetPathNormalizer.Normalize(e.OldFullPath);
                var newPath = AssetPathNormalizer.Normalize(e.FullPath);

                if (_repository.TryGet(oldPath, out var asset))
                {
                    _repository.Remove(oldPath);
                    _assetCache.Remove(oldPath);

                    // Update path in our map
                    _idToPathMap[asset.ID] = Path.GetRelativePath(BasePath, e.FullPath);

                    // Since IAsset.Path is read-only, we need to create a new asset with the updated path
                    // or use a workaround. For now, we'll remove and re-add with updated path.
                    _repository.Remove(asset.ID);

                    // Create new asset path
                    var newAssetPath = new AssetPath(newPath);

                    // We need to reload the asset with the new path
                    var newAsset = await _loader.LoadAssetAsync<IAsset>(Path.GetRelativePath(BasePath, e.FullPath));
                    _repository.Add(newAsset);
                    CacheAsset(newAsset);

                    OnAssetChanged?.Invoke(new AssetChangedEventArgs
                    {
                        Asset = newAsset,
                        ChangeType = AssetChangeType.Renamed,
                        Path = newPath
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to handle file rename from {OldPath} to {NewPath}", e.OldFullPath, e.FullPath);
            }
        }

        #endregion

        #region Project Management

        private async Task SetCurrentProjectAsync(IProject project)
        {
            _currentProject = project;
            _loader.SetBasePath(BasePath);
            InitializeFileWatcher();
            _repository.Add(project);

            // Build initial ID to path map
            await BuildIdToPathMapAsync();

            _logger.Info("Project loaded: {ProjectName} at {BasePath}", project.Name, BasePath);
            OnProjectLoaded?.Invoke(project);
        }

        private async Task BuildIdToPathMapAsync()
        {
            if (string.IsNullOrEmpty(BasePath) || !Directory.Exists(BasePath))
                return;

            var assetFiles = Directory.GetFiles(BasePath, "*" + AssetConstants.AssetExtension, SearchOption.AllDirectories);

            var tasks = assetFiles.Select(async file =>
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var header = await _serializer.DeserializeHeaderAsync(stream);
                    var relativePath = Path.GetRelativePath(BasePath, file);
                    _idToPathMap[header.AssetId] = relativePath;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to read header from {FilePath}", file);
                }
            });

            await Task.WhenAll(tasks);
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            _cts.Cancel();
            _fileWatcher?.Dispose();
            _assetCache.Dispose();
            _loadSemaphore.Dispose();
            GC.SuppressFinalize(this);
        }


        #endregion
    }
}