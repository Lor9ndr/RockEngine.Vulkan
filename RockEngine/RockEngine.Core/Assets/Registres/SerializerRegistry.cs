using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Assets.Serializers;

using System.Collections.Concurrent;

namespace RockEngine.Core.Assets.Registres
{
    public class SerializerRegistry : IRegistry<IAssetSerializer<IAssetData>>
    {
        private readonly ConcurrentDictionary<Type, IAssetSerializer<IAssetData>> _serializers = new();

        public void Register<TKey>(TKey dataType, IAssetSerializer<IAssetData> serializer)
        {
            _serializers[(Type)(object)dataType] = serializer;
        }

        public IAssetSerializer<IAssetData> Get<TKey>(TKey dataType)
        {
            return _serializers[(Type)(object)dataType];
        }

        public bool TryGet<TKey>(TKey dataType, out IAssetSerializer<IAssetData> serializer)
        {
            return _serializers.TryGetValue((Type)(object)dataType, out serializer);
        }
    }

}
