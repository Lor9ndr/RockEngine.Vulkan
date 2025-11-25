using NLog;

using RockEngine.Core.Assets.Json;

using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets.Serializers
{
    public sealed class SystemTextJsonAssetSerializer : IAssetSerializer
    {
        private readonly JsonSerializerOptions _options;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public JsonSerializerOptions Options => _options;

        public SystemTextJsonAssetSerializer(IEnumerable<JsonConverter> converters, IServiceProvider container)
        {
            _options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                WriteIndented = false,
                IncludeFields = false,
                PropertyNameCaseInsensitive = true,
                TypeInfoResolver = new SystemTextJsonContractResolver(container),
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            foreach (var converter in converters)
            {
                _options.Converters.Add(converter);
            }
        }

        public async Task SerializeAsync(IAsset asset, Stream stream)
        {
            try
            {
                // Use reflection to create properly typed wrapper
                var wrapperType = typeof(AssetWrapper<>).MakeGenericType(asset.GetType());
                var wrapper = Activator.CreateInstance(wrapperType);
                wrapper.GetType().GetProperty(nameof(AssetWrapper<>.Asset)).SetValue(wrapper, asset);
                wrapper.GetType().GetProperty(nameof(AssetWrapper<>.Metadata)).SetValue(wrapper, new AssetMetadata(asset));
                wrapper.GetType().GetProperty(nameof(AssetWrapper<>.AssetData)).SetValue(wrapper, asset.GetData());

                await JsonSerializer.SerializeAsync(stream, wrapper, wrapperType, _options);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Asset serialization failed for asset {AssetId}", asset.ID);
                throw new AssetSerializationException($"Failed to serialize asset {asset.Name}", ex);
            }
        }

        public async Task<AssetMetadata> DeserializeMetadataAsync(Stream stream)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(stream);
                var root = document.RootElement;

                if (root.TryGetProperty("metadata", out var metadataElement))
                {
                    return JsonSerializer.Deserialize<AssetMetadata>(metadataElement.GetRawText(), _options);
                }

                throw new AssetSerializationException("Could not find metadata in asset stream");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize asset metadata");
                throw new AssetSerializationException("Failed to deserialize asset metadata", ex);
            }
        }

        public async Task<object> DeserializeDataAsync(Stream stream, Type dataType)
        {
            if (stream.CanSeek)
                stream.Position = 0;

            try
            {
                using var document = await JsonDocument.ParseAsync(stream);
                var root = document.RootElement;
                
                if (root.TryGetProperty("assetData", out var metadataElement))
                {
                    return JsonSerializer.Deserialize(metadataElement.GetRawText(), dataType, _options);
                }
                throw new AssetSerializationException($"Failed to deserialize asset data for type {dataType.Name}, data property not found, total properties: {root.GetPropertyCount()}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize data for type {DataType}", dataType.Name);
                throw new AssetSerializationException($"Failed to deserialize asset data for type {dataType.Name}", ex);
            }
        }

        public async Task<object> DeserializeAssetAsync(Stream stream, Type assetType)
        {
            if (stream.CanSeek)
                stream.Position = 0;

            try
            {
                var wrapperType = typeof(AssetWrapper<>).MakeGenericType(assetType);
                var wrapper = await JsonSerializer.DeserializeAsync(stream, wrapperType, _options);
                var dataProperty = wrapperType.GetProperty(nameof(AssetWrapper<>.Asset));
                return dataProperty.GetValue(wrapper);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize asset for type {DataType}", assetType.Name);
                throw new AssetSerializationException($"Failed to deserialize asset for type {assetType.Name}", ex);
            }
        }

        // Helper method to get asset type from metadata
        public async Task<Type> GetAssetTypeAsync(Stream stream)
        {
            try
            {
                var metadata = await DeserializeMetadataAsync(stream);
                return metadata.AssetType ?? throw new AssetSerializationException($"Unknown asset type: {metadata.AssetType}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get asset type from stream");
                throw new AssetSerializationException("Failed to determine asset type", ex);
            }
        }
    }


    // Generic wrapper for type-safe serialization
    public class AssetWrapper<T>  where T : class, IAsset
    {
        public AssetMetadata Metadata { get; set; }
        public T Asset { get; set; }
        public object AssetData { get;set;}

        public AssetWrapper() { }

        public AssetWrapper(AssetMetadata metadata, T asset, object assetData)
        {
            Metadata = metadata;
            AssetData = assetData;
            Asset = asset;
        }
    }
}