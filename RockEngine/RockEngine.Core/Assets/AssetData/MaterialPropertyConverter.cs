
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RockEngine.Core.Assets.AssetData
{
    public class MaterialPropertyConverter : JsonConverter<MaterialProperty>
    {
        public override MaterialProperty ReadJson(JsonReader reader, Type objectType, MaterialProperty existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            return new MaterialProperty
            {
                Type = obj["type"].ToObject<MaterialPropertyType>(),
                Value = obj["value"].ToObject<object>()
            };
        }

        public override void WriteJson(JsonWriter writer, MaterialProperty value, JsonSerializer serializer)
        {
            var obj = new JObject
            {
                ["type"] = JToken.FromObject(value.Type),
                ["value"] = JToken.FromObject(value.Value)
            };
            obj.WriteTo(writer);
        }
    }
}