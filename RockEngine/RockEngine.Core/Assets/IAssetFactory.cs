namespace RockEngine.Core.Assets
{
    public delegate IAsset AssetFactoryDelegate(string name, string path);

    public class AssetFactoryRegistry
    {
        private readonly Dictionary<string, AssetFactoryDelegate> _factories = new();

        public void RegisterFactory(string assetType, AssetFactoryDelegate factory)
        {
            _factories[assetType] = factory;
        }

        public IAsset CreateAsset(string assetType, string name, string path)
        {
            if (_factories.TryGetValue(assetType, out var factory))
            {
                return factory(name, path);
            }
            throw new InvalidOperationException($"No factory registered for asset type: {assetType}");
        }
    }
}