using RockEngine.Core.Attributes;
using RockEngine.Core.DI;

namespace RockEngine.Core.Assets
{
    public class AssetReference<T> where T : class, IAsset
    {
        private Guid _assetId;
        private T _asset;
        private bool _isResolved;

        public Guid AssetID
        {
            get => _assetId;
            set
            {
                _assetId = value;
                _asset = null;
                _isResolved = false;
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
            if (_isResolved && _asset != null) return;

            var assetManager = IoC.Container.GetInstance<AssetManager>();
            _asset = assetManager.GetAsset<T>(_assetId);

            if (_asset == null)
                throw new Exception($"Asset not found: {_assetId}");

            _isResolved = true;
        }

        public static implicit operator T(AssetReference<T> reference) => reference.Asset;
        public static implicit operator AssetReference<T>(T asset) => new AssetReference<T>(asset);
        public static implicit operator Guid(AssetReference<T> reference) => reference.AssetID;
        public static implicit operator AssetReference<T>(Guid assetId) => new AssetReference<T>(assetId);
    }
}
