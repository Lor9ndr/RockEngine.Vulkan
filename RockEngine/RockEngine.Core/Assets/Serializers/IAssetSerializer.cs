using System.Text.Json;

namespace RockEngine.Core.Assets.Serializers
{
    public interface IAssetSerializer
    {
        public JsonSerializerOptions Options { get;}
        Task SerializeAsync(IAsset asset, Stream stream);
        Task<AssetMetadata> DeserializeMetadataAsync(Stream stream);
        Task<object> DeserializeDataAsync(Stream stream, Type dataType);
        Task<object> DeserializeAssetAsync(Stream stream, Type assetType);
    }
}