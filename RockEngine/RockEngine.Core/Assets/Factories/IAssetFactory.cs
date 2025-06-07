using RockEngine.Core.Assets.AssetData;

namespace RockEngine.Core.Assets.Factories
{
    public interface IAssetFactory<TData, TAsset>
        where TData : IAssetData
        where TAsset : IAsset
    {
        TAsset CreateAsset(string path, TData data);
    }

}
