using Newtonsoft.Json;

using NLog;

using RockEngine.Core.Assets.Json;

using System.Buffers;
using System.Text;

namespace RockEngine.Core.Assets.Serializers
{
    public sealed class JsonAssetSerializer : IAssetSerializer
    {
        private readonly JsonSerializer _serializer;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Pool for buffers
        private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
        private const int BufferSize = 4096;

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
                ContractResolver = new JsonContractResolver(),
            });
        }

        public async Task SerializeAsync(IAsset asset, Stream stream)
        {
            byte[] buffer = _bufferPool.Rent(BufferSize);
            try
            {
                var wrapper = new
                {
                    Metadata = asset,
                    Data = asset.GetData()
                };

                using var writer = new StreamWriter(stream, Encoding.UTF8, BufferSize, true);
                using var jsonWriter = new JsonTextWriter(writer)
                {
                    ArrayPool = JsonArrayPool.Instance,
                    CloseOutput = false
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
            byte[] buffer = _bufferPool.Rent(BufferSize);
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, true, BufferSize, true);
                using var jsonReader = new JsonTextReader(reader)
                {
                    ArrayPool = JsonArrayPool.Instance,
                    CloseInput = false
                };

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
            finally
            {
                _bufferPool.Return(buffer);
            }
        }

        public async Task<object> DeserializeDataAsync(Stream stream, Type dataType)
        {
            byte[] buffer = _bufferPool.Rent(BufferSize);
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, true, BufferSize, true);
                using var jsonReader = new JsonTextReader(reader)
                {
                    ArrayPool = JsonArrayPool.Instance,
                    CloseInput = false
                };

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
            finally
            {
                _bufferPool.Return(buffer);
            }
        }
    }

    // JSON array pool for better memory management
    public sealed class JsonArrayPool : IArrayPool<char>
    {
        public static readonly JsonArrayPool Instance = new JsonArrayPool();

        public char[] Rent(int minimumLength)
        {
            return ArrayPool<char>.Shared.Rent(minimumLength);
        }

        public void Return(char[] array)
        {
            ArrayPool<char>.Shared.Return(array);
        }
    }
}