using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RockEngine.Core.ResourceProviders;

namespace RockEngine.Core.Assets.Converters
{
  
    public sealed class MaterialResourceProviderConverter : JsonConverter<MaterialProvider>
    {
        public override bool CanWrite => true;
        public override bool CanRead => true;

        public override MaterialProvider ReadJson(JsonReader reader, Type objectType, MaterialProvider existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);

            if (obj.TryGetValue("IsAssetBased", out var isAssetToken) && isAssetToken.Value<bool>())
            {
                var assetRef = obj["AssetReference"]?.ToObject<AssetReference<MaterialAsset>>(serializer);
                return assetRef != null ? new MaterialProvider(assetRef) : null;
            }
            else
            {
                // For direct materials, we might serialize material data
                // or leave it to the component to recreate
                return null;
            }
        }

        public override void WriteJson(JsonWriter writer, MaterialProvider value, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("IsAssetBased");
            writer.WriteValue(value.IsAssetBased);

            if (value.IsAssetBased && value.AssetReference != null)
            {
                writer.WritePropertyName("AssetReference");
                serializer.Serialize(writer, value.AssetReference);
            }

            writer.WriteEndObject();
        }
    }
}