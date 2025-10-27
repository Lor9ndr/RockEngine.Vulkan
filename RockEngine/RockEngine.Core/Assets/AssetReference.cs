using RockEngine.Core.Attributes;
using RockEngine.Core.DI;

namespace RockEngine.Core.Assets
{
    public class AssetReference<T> where T : class, IAsset
    {
        private Guid _assetId;
        private T _asset;
        private bool _isResolved;
        private Task<T> _loadingTask;

        public Guid AssetID
        {
            get => _assetId;
            set
            {
                _assetId = value;
                _asset = null;
                _isResolved = false;
                _loadingTask = null;
            }
        }

        [SerializeIgnore]
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
                _asset = value;
                _assetId = value?.ID ?? Guid.Empty;
                _isResolved = value is not null;
                _loadingTask = Task.FromResult(value);
            }
        }


        public AssetReference() { }

        public AssetReference(T asset)
        {
            Asset = asset;
        }

        public AssetReference(Guid assetId)
        {
            AssetID = assetId;
        }

        private void Resolve()
        {
            if (_isResolved && _asset != null)
            {
                return;
            }

            var assetManager = IoC.Container.GetInstance<AssetManager>();

            // Try synchronous first
            if (assetManager.TryGetAsset<T>(_assetId, out var foundAsset))
            {
                _asset = foundAsset;
                _isResolved = true;
                return;
            }

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

            var assetManager = IoC.Container.GetInstance<AssetManager>();
            _loadingTask = assetManager.GetAssetAsync<T>(_assetId);
            _asset = await _loadingTask;
            _isResolved = true;
            return _asset;
        }

        // Safe getter that doesn't throw
        public bool TryGetAsset(out T asset)
        {
            if (_isResolved && _asset != null)
            {
                asset = _asset;
                return true;
            }

            var assetManager = IoC.Container.GetInstance<AssetManager>();
            if (assetManager.TryGetAsset<T>(_assetId, out asset))
            {
                _asset = asset;
                _isResolved = true;
                return true;
            }

            asset = null;
            return false;
        }

        public static implicit operator T(AssetReference<T> reference) => reference.Asset;
        public static implicit operator AssetReference<T>(T asset) => new AssetReference<T>(asset);
        public static implicit operator Guid(AssetReference<T> reference) => reference.AssetID;
        public static implicit operator AssetReference<T>(Guid assetId) => new AssetReference<T>(assetId);
    }
}