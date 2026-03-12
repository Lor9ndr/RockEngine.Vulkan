using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace RockEngine.Assets
{
    public class AssetRepository : IAssetRepository
    {
        private readonly ConcurrentDictionary<Guid, IAsset> _assetsById = new();
        private readonly ConcurrentDictionary<string, Guid> _pathToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Type, ImmutableHashSet<Guid>> _assetsByType = new();
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

        public void Add(IAsset asset)
        {
            var pathKey = AssetPathNormalizer.Normalize(asset.Path.ToString());

            _lock.EnterWriteLock();
            try
            {
                if (_assetsById.TryAdd(asset.ID, asset))
                {
                    _pathToId[pathKey] = asset.ID;

                    _assetsByType.AddOrUpdate(asset.GetType(),
                        ImmutableHashSet.Create(asset.ID),
                        (_, set) => set.Add(asset.ID));
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool TryGet(Guid id, out IAsset asset)
        {
            _lock.EnterReadLock();
            try
            {
                return _assetsById.TryGetValue(id, out asset);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public bool TryGet(string path, out IAsset asset)
        {
            var normalizedPath = AssetPathNormalizer.Normalize(path);

            _lock.EnterReadLock();
            try
            {
                if (_pathToId.TryGetValue(normalizedPath, out var id))
                    return _assetsById.TryGetValue(id, out asset);

                asset = null;
                return false;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IEnumerable<IAsset> GetAssetsOfType<T>() where T : IAsset
        {
            _lock.EnterReadLock();
            try
            {
                if (_assetsByType.TryGetValue(typeof(T), out var ids))
                    return ids.Select(id => _assetsById.TryGetValue(id, out var asset) ? asset : null)
                             .Where(asset => asset != null)!;

                return Enumerable.Empty<IAsset>();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IEnumerable<IAsset> GetAll() => _assetsById.Values;

        public void Remove(Guid id)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_assetsById.TryRemove(id, out var asset))
                {
                    var pathKey = AssetPathNormalizer.Normalize(asset.Path.ToString());
                    _pathToId.TryRemove(pathKey, out _);

                    if (_assetsByType.TryGetValue(asset.GetType(), out var ids))
                    {
                        ids = ids.Remove(id);
                        _assetsByType[asset.GetType()] = ids;
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Remove(string path)
        {
            var normalizedPath = AssetPathNormalizer.Normalize(path);

            _lock.EnterWriteLock();
            try
            {
                if (_pathToId.TryRemove(normalizedPath, out var id))
                {
                    if (_assetsById.TryRemove(id, out var asset))
                    {
                        if (_assetsByType.TryGetValue(asset.GetType(), out var ids))
                        {
                            ids = ids.Remove(id);
                            _assetsByType[asset.GetType()] = ids;
                        }
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _assetsById.Clear();
                _pathToId.Clear();
                _assetsByType.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }
}