using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NLog;

using System.Text;

namespace RockEngine.Core.Assets.Serializers
{
    public sealed class JsonAssetSerializer : IAssetSerializer
    {
        private readonly JsonSerializerSettings _settings;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public JsonAssetSerializer(IEnumerable<JsonConverter> converters)
        {
            _settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Objects,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Converters = converters.ToList()
            };
        }

        public async Task SerializeAsync(IAsset asset, Stream stream)
        {
            try
            {
                // Create wrapper with metadata and data
                var wrapper = new
                {
                    Metadata = asset,
                    Data = asset.GetData()
                };

                await using var writer = new StreamWriter(stream, leaveOpen: true);
                var json = JsonConvert.SerializeObject(wrapper, _settings);
                await writer.WriteAsync(json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Asset serialization failed");
                throw new AssetSerializationException("Failed to serialize asset", ex);
            }
        }

        public async Task<IAsset> DeserializeMetadataAsync(Stream stream)
        {
            try
            {
                using var reader = new StreamReader(stream, leaveOpen: true);
                var json = await reader.ReadToEndAsync();
                var jObject = JObject.Parse(json);

                return jObject["Metadata"]!.ToObject<IAsset>(JsonSerializer.Create(_settings));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Metadata deserialization failed");
                throw new AssetSerializationException("Failed to deserialize metadata", ex);
            }
        }

        public async Task<object> DeserializeDataAsync(Stream stream, Type dataType)
        {
            try
            {
                using var reader = new StreamReader(stream, leaveOpen: true);
                var json = await reader.ReadToEndAsync();
                var jObject = JObject.Parse(json);

                return jObject["Data"]!.ToObject(dataType, JsonSerializer.Create(_settings));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Data deserialization failed");
                throw new AssetSerializationException("Failed to deserialize data", ex);
            }
        }
    }

    public class AssetSerializationException : Exception
    {
        public AssetSerializationException(string message, Exception inner)
            : base(message, inner) { }
    }
}