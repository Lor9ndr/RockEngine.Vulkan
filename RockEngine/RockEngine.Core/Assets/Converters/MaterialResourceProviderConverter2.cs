using System.Text.Json;
using System.Text.Json.Serialization;

using RockEngine.Core.ResourceProviders;

namespace RockEngine.Core.Assets.Converters
{
    public sealed class MaterialResourceProviderConverter2 : JsonConverter<MaterialProvider>
    {
        public override MaterialProvider Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.TryGetProperty("IsAssetBased", out var isAssetToken) &&
                isAssetToken.ValueKind == JsonValueKind.True)
            {
                if (root.TryGetProperty("AssetReference", out var assetRefElement))
                {
                    var assetRef = assetRefElement.Deserialize<AssetReference<MaterialAsset>>(options);
                    return assetRef != null ? new MaterialProvider(assetRef) : null;
                }
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, MaterialProvider value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WriteBoolean("IsAssetBased", value.IsAssetBased);

            if (value.IsAssetBased && value.AssetReference != null)
            {
                writer.WritePropertyName("AssetReference");
                JsonSerializer.Serialize(writer, value.AssetReference, options);
            }

            writer.WriteEndObject();
        }
    }
}