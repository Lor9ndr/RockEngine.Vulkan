using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Assets.Factories;

using System.Collections.Concurrent;

namespace RockEngine.Core.Assets.Registres
{
    public class FactoryRegistry //: IRegistry<IAssetFactory<IAssetData, IAsset>>
    {
        private readonly ConcurrentDictionary<Type, IAssetFactory<IAssetData, IAsset>> _factories = new();

        public void Register<TKey>(TKey dataType, IAssetFactory<IAssetData, IAsset> factory)
        {
            _factories[(Type)(object)dataType] = factory;
        }

        public IAssetFactory<IAssetData, IAsset> Get<TKey>(TKey dataType)
        {
            return _factories[(Type)(object)dataType];
        }

        public bool TryGet<TKey>(TKey dataType, out IAssetFactory<IAssetData, IAsset> factory)
        {
            return _factories.TryGetValue((Type)(object)dataType, out factory);
        }
    }

}
