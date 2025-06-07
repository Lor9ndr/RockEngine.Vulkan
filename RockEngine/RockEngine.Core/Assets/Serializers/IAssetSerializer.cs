using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets.Serializers
{
    public interface IAssetSerializer<TData> where TData : IAssetData
    {
        TData Deserialize(string json);
        string Serialize(TData data);
    }

}
