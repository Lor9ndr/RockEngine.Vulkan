using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RockEngine.Core.Rendering;
using RockEngine.Core.ResourceProviders;

namespace RockEngine.Core.Assets.Converters
{
    public sealed class MeshResourceProviderConverter : JsonConverter<MeshProvider>
    {
        public override bool CanWrite => true;
        public override bool CanRead => true;

        public override MeshProvider ReadJson(JsonReader reader, Type objectType, MeshProvider existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);

            if (obj.TryGetValue("IsAssetBased", out var isAssetToken) && isAssetToken.Value<bool>())
            {
                var assetRef = obj["AssetReference"]?.ToObject<AssetReference<MeshAsset>>(serializer);
                return assetRef != null ? new MeshProvider(assetRef) : null;
            }
            else
            {
                // For direct meshes, we might not serialize the actual mesh data
                // Instead, the component that creates it should recreate it
                return null;
            }
        }

        public override void WriteJson(JsonWriter writer, MeshProvider value, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("IsAssetBased");
            writer.WriteValue(value.IsAssetBased);

            if (value.IsAssetBased && value.AssetReference != null)
            {
                writer.WritePropertyName("AssetReference");
                serializer.Serialize(writer, value.AssetReference);
            }
            // Don't serialize direct meshes - they should be recreated by the component

            writer.WriteEndObject();
        }
    }

}

