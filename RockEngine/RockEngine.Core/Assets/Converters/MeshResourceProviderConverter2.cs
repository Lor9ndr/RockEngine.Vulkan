using RockEngine.Core.ResourceProviders;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets.Converters
{
    public sealed class MeshResourceProviderConverter2 : JsonConverter<MeshProvider>
    {
        public override MeshProvider Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.TryGetProperty("IsAssetBased", out var isAssetToken) &&
                isAssetToken.ValueKind == JsonValueKind.True)
            {
                if (root.TryGetProperty("AssetReference", out var assetRefElement))
                {
                    var assetRef = assetRefElement.Deserialize<AssetReference<MeshAsset>>(options);
                    return assetRef != null ? new MeshProvider(assetRef) : null;
                }
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, MeshProvider value, JsonSerializerOptions options)
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
