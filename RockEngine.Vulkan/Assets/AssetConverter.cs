using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockEngine.Vulkan.Assets
{
    public class AssetConverter : JsonConverter<IAsset>
    {
        private readonly AssetManager _assetManager;

        public AssetConverter(AssetManager assetManager)
        {
            _assetManager = assetManager;
        }

        public override IAsset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var jsonObject = JsonDocument.ParseValue(ref reader).RootElement;
            var id = jsonObject.GetProperty("ID").GetGuid();
            var path = jsonObject.GetProperty("Path").GetString() ?? throw new JsonException("Path cannot be null.");

            var asset = _assetManager.GetAssetByIdAsync<IAsset>(id, path).GetAwaiter().GetResult();
            return asset;
        }

        public override void Write(Utf8JsonWriter writer, IAsset value, JsonSerializerOptions options)
        {
            if (value is IAsset asset)
            {
                writer.WriteStartObject();
                writer.WriteString("ID", asset.ID);
                writer.WriteString("Path", asset.Path);
                writer.WriteEndObject();
            }
            else
            {
                throw new JsonException("The object does not implement IAsset interface.");
            }
        }
    }

    public class AssetEnumerableConverter : JsonConverter<IEnumerable<IAsset>>
    {
        private readonly AssetManager _assetManager;

        public AssetEnumerableConverter(AssetManager assetManager)
        {
            _assetManager = assetManager;
        }

        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeof(IEnumerable<IAsset>).IsAssignableFrom(typeToConvert))
            {
                return false;
            }

            if (!typeToConvert.IsGenericType)
            {
                return false;
            }

            var genericArgument = typeToConvert.GetGenericArguments()[0];
            return typeof(IAsset).IsAssignableFrom(genericArgument);
        }
        public override IEnumerable<IAsset> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var jsonArray = JsonDocument.ParseValue(ref reader).RootElement;
            var assets = new List<IAsset>();

            foreach (var jsonObject in jsonArray.EnumerateArray())
            {
                var id = jsonObject.GetProperty("ID").GetGuid();
                var path = jsonObject.GetProperty("Path").GetString() ?? throw new JsonException("Path cannot be null.");

                var asset = _assetManager.GetAssetByIdAsync<IAsset>(id, path).GetAwaiter().GetResult();
                assets.Add(asset);
            }

            return assets;
        }

        public override void Write(Utf8JsonWriter writer, IEnumerable<IAsset> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();

            foreach (var item in value)
            {
                if (item is IAsset asset)
                {
                    writer.WriteStartObject();
                    writer.WriteString("ID", asset.ID);
                    writer.WriteString("Path", asset.Path);
                    writer.WriteEndObject();
                }
                else
                {
                    throw new JsonException("One or more objects in the collection do not implement IAsset interface.");
                }
            }

            writer.WriteEndArray();
        }
    }
}