using Newtonsoft.Json;

namespace RockEngine.Core.Assets.Converters
{
    public class AssetPathConverter : JsonConverter<AssetPath>
    {
        public override void WriteJson(JsonWriter writer, AssetPath value, JsonSerializer serializer)
        {
            writer.WriteValue(value.FullPath);
        }

        public override AssetPath ReadJson(JsonReader reader, Type objectType, AssetPath existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            return new AssetPath(reader.Value?.ToString() ?? string.Empty);
        }
    }
}
