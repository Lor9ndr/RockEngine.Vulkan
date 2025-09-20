using Newtonsoft.Json;

namespace RockEngine.Core.Assets.Serializers
{
    public interface IAssetSerializer
    {
        JsonSerializer Serializer { get; }

        Task SerializeAsync(IAsset asset, Stream stream);
        Task<IAsset> DeserializeMetadataAsync(Stream stream);
        Task<object> DeserializeDataAsync(Stream stream, Type dataType);
    }
}