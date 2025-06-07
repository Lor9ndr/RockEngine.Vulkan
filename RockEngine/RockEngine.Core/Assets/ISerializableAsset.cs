using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets
{
    public interface ISerializableAsset<TData> where TData : IAssetData
    {
        TData GetData();
        void UpdateData(TData data);
    }

}
