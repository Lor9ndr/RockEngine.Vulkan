using MessagePack;

using RockEngine.Assets;
using RockEngine.Core.DI;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public class AssetReference<T> : IAssetReference<T> where T : class, IAsset
    {
        private Guid _assetId;
        private T _asset;
        private bool _isResolved;
        private Task<T> _loadingTask;

        [Key(1)]
        public Guid AssetID => _assetId;

        [IgnoreMember]
        public T Asset
        {
            get
            {
                if (!_isResolved && _assetId != Guid.Empty)
                {
                    Resolve();
                }
                return _asset;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _asset = value;
                _assetId = value.ID;
                _isResolved = true;
                _loadingTask = Task.FromResult(value!);
            }
        }
        [IgnoreMember]

        public bool IsResolved =>_isResolved;

        public T Get()
        {
            if (!_isResolved && _assetId != Guid.Empty)
            {
                Resolve();
            }
            return _asset;
        }

        private void Set(T asset)
        {
            ArgumentNullException.ThrowIfNull(asset);
            _asset = asset;
            _assetId = asset.ID;
            _isResolved = asset is not null;
            _loadingTask = Task.FromResult(asset!);
        }

        public AssetReference() { }

        public AssetReference(T asset)
        {
            Set(asset);
        }

        public AssetReference(Guid assetId)
        {
            _assetId = assetId;
        }

        private void Resolve()
        {
            if (_isResolved && _asset != null)
            {
                return;
            }

            var assetRepository = IoC.Container.GetInstance<IAssetRepository>();
            if (assetRepository.TryGet(_assetId, out var asset))
            {
                Set((T)asset);
                return;
            }
            var assetManager = IoC.Container.GetInstance<IAssetManager>();

            // If not found synchronously, start async loading but don't wait
            _loadingTask = assetManager.GetAssetAsync<T>(_assetId);
            _loadingTask.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    _asset = task.Result;
                    _isResolved = true;
                }
            }, TaskScheduler.Default);

        }

        // Async method for explicit async loading
        public async Task<T> GetAssetAsync()
        {
            if (_isResolved && _asset != null)
            {
                return _asset;
            }

            if (_loadingTask != null)
            {
                return await _loadingTask;
            }

            var assetManager = IoC.Container.GetInstance<IAssetManager>();
            _loadingTask = assetManager.GetAssetAsync<T>(_assetId);
            _asset = await _loadingTask;
            _isResolved = true;
            return _asset;
        }



        public static implicit operator T(AssetReference<T> reference) => reference.Asset;
        public static implicit operator AssetReference<T>(T asset) => new AssetReference<T>(asset);
        public static implicit operator Guid(AssetReference<T> reference) => reference.AssetID;
        public static implicit operator AssetReference<T>(Guid assetId) => new AssetReference<T>(assetId);

        public static explicit operator AssetReference<T>(AssetReference<IAsset> v)
        {
            if (v._isResolved)
            {
                return new AssetReference<T>((T)v.Asset);
            }
            return new AssetReference<T>(v.AssetID);
        }
    }
}
