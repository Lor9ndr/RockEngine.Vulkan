using Newtonsoft.Json;

using NLog;

using RockEngine.Core.Assets.Json;

using System.Text;

namespace RockEngine.Core.Assets.Serializers
{
    public sealed class JsonAssetSerializer : IAssetSerializer
    {
        private readonly JsonSerializer _serializer;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public JsonSerializer Serializer => _serializer;

        public JsonAssetSerializer(IEnumerable<JsonConverter> converters)
        {
            _serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Objects,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                NullValueHandling = NullValueHandling.Include,
                Converters = converters.ToList(),
                ContractResolver = new JsonContractResolver()
            });
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

                using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true);
                using var jsonWriter = new JsonTextWriter(writer);

                _serializer.Serialize(jsonWriter, wrapper);
                await jsonWriter.FlushAsync().ConfigureAwait(false);
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
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
                using var jsonReader = new JsonTextReader(reader);

                while (await jsonReader.ReadAsync().ConfigureAwait(false))
                {
                    if (jsonReader.TokenType == JsonToken.PropertyName &&
                        jsonReader.Value?.ToString() == "Metadata")
                    {
                        await jsonReader.ReadAsync().ConfigureAwait(false);
                        return _serializer.Deserialize<IAsset>(jsonReader);
                    }
                }

                throw new AssetSerializationException("Metadata property not found in asset file");
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
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
                using var jsonReader = new JsonTextReader(reader);

                while (await jsonReader.ReadAsync().ConfigureAwait(false))
                {
                    if (jsonReader.TokenType == JsonToken.PropertyName &&
                        jsonReader.Value?.ToString() == "Data")
                    {
                        await jsonReader.ReadAsync().ConfigureAwait(false);
                        return _serializer.Deserialize(jsonReader, dataType);
                    }
                }

                throw new AssetSerializationException("Data property not found in asset file");
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

        public AssetSerializationException(string message)
            : base(message) { }
    }
}