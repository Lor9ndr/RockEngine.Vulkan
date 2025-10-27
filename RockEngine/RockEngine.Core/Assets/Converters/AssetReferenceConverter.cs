using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RockEngine.Core.Assets.Converters
{
    public class AssetReferenceConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType.IsGenericType &&
                   objectType.GetGenericTypeDefinition() == typeof(AssetReference<>);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            // Get the generic type argument (T in AssetReference<T>)
            Type assetType = objectType.GetGenericArguments()[0];

            // Create a new instance of AssetReference<T>
            var reference = Activator.CreateInstance(objectType);

            if (reader.TokenType == JsonToken.String)
            {
                // Handle GUID string
                string guidString = reader.Value.ToString();
                if (Guid.TryParse(guidString, out Guid assetId))
                {
                    var assetIdProperty = objectType.GetProperty("AssetID");
                    assetIdProperty.SetValue(reference, assetId);
                }
            }
            else if (reader.TokenType == JsonToken.StartObject)
            {
                // Handle object with AssetID property
                JObject jo = JObject.Load(reader);
                if (jo["AssetID"] != null)
                {
                    var assetIdProperty = objectType.GetProperty("AssetID");
                    assetIdProperty.SetValue(reference, jo["AssetID"].ToObject<Guid>());
                }
            }

            return reference;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            // Get the AssetID property value
            var assetIdProperty = value.GetType().GetProperty("AssetID");
            Guid assetId = (Guid)assetIdProperty.GetValue(value);

            // Write just the GUID as a string
            writer.WriteValue(assetId.ToString());
        }
    }
}
