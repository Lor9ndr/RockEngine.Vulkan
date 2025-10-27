using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using NLog;

using System.Buffers;
using System.Text;

namespace RockEngine.Core.Assets.Serializers
{
    public sealed class OptimizedJsonAssetSerializer : IAssetSerializer
    {
        private readonly JsonSerializer _serializer;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Pools for optimal memory usage
        private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
        private const int _bufferSize = 65536;

        public JsonSerializer Serializer => _serializer;

        public OptimizedJsonAssetSerializer(IEnumerable<JsonConverter> converters)
        {
            _serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = Formatting.None, // No formatting for smaller files
                TypeNameHandling = TypeNameHandling.Auto,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore, // Faster than Error
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Converters = converters.ToList(),
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }

        public async Task SerializeAsync(IAsset asset, Stream stream)
        {
            byte[] buffer = _bufferPool.Rent(_bufferSize);
            try
            {
                var wrapper = new AssetWrapper { Metadata = asset, Data = asset.GetData() };

                using var writer = new StreamWriter(stream, Encoding.UTF8, _bufferSize, true);
                using var jsonWriter = new JsonTextWriter(writer)
                {
                    ArrayPool = JsonArrayPool.Instance,
                    CloseOutput = false,
                    AutoCompleteOnClose = false
                };

                _serializer.Serialize(jsonWriter, wrapper);
                await jsonWriter.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Asset serialization failed");
                throw new AssetSerializationException("Failed to serialize asset", ex);
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }

        public async Task<IAsset> DeserializeMetadataAsync(Stream stream)
        {
            // For small streams, read entirely for faster access
            if (stream.Length < 1024 * 1024) // 1MB threshold
            {
                return await DeserializeMetadataFastAsync(stream);
            }

            // For large streams, use buffered reading
            return await DeserializeMetadataBufferedAsync(stream);
        }

        private async Task<IAsset> DeserializeMetadataFastAsync(Stream stream)
        {
            byte[] buffer = _bufferPool.Rent((int)stream.Length);
            try
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, (int)stream.Length));
                var data = buffer.AsSpan(0, bytesRead);

                using var memoryStream = new MemoryStream(buffer, 0, bytesRead);
                using var reader = new StreamReader(memoryStream, Encoding.UTF8);
                using var jsonReader = new JsonTextReader(reader)
                {
                    ArrayPool = JsonArrayPool.Instance,
                    CloseInput = false
                };

                return await Task.Run(() => DeserializeMetadataWithReader(jsonReader));
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }

        private async Task<IAsset> DeserializeMetadataBufferedAsync(Stream stream)
        {
            byte[] buffer = _bufferPool.Rent(_bufferSize);
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, true, _bufferSize, true);
                using var jsonReader = new JsonTextReader(reader)
                {
                    ArrayPool = JsonArrayPool.Instance,
                    CloseInput = false
                };

                return await Task.Run(() => DeserializeMetadataWithReader(jsonReader));
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }

        private IAsset DeserializeMetadataWithReader(JsonTextReader jsonReader)
        {
            while (jsonReader.Read())
            {
                if (jsonReader.TokenType == JsonToken.PropertyName)
                {
                    var value = jsonReader.Value;
                    if (value is not null && value.ToString()!.Equals("Metadata", StringComparison.OrdinalIgnoreCase))
                    {
                        jsonReader.Read();
                        return _serializer.Deserialize<IAsset>(jsonReader);
                    }
                    
                }
            }
            throw new AssetSerializationException("Metadata property not found in asset file");
        }

        public async Task<object> DeserializeDataAsync(Stream stream, Type dataType)
        {
            // Reset stream position for data reading
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            byte[] buffer = _bufferPool.Rent(_bufferSize);
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, true, _bufferSize, true);
                using var jsonReader = new JsonTextReader(reader)
                {
                    ArrayPool = JsonArrayPool.Instance,
                    CloseInput = false
                };

                while (await jsonReader.ReadAsync().ConfigureAwait(false))
                {
                    if (jsonReader.TokenType == JsonToken.PropertyName)
                    {
                        var value = jsonReader.Value;
                        if (value is not null && value.ToString()!.Equals("data", StringComparison.OrdinalIgnoreCase))
                        {
                            await jsonReader.ReadAsync().ConfigureAwait(false);
                            return _serializer.Deserialize(jsonReader, dataType);
                        }
                    }
                }

                throw new AssetSerializationException("Data property not found in asset file");
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }
    }

    // Supporting types
    public class AssetWrapper
    {
        public IAsset Metadata { get; set; }
        public object Data { get; set; }
    }
}