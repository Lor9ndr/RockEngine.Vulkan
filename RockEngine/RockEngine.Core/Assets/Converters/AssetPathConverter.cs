using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets.Converters
{
    public class AssetPathConverter2 : JsonConverter<AssetPath>
    {
        public override AssetPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new AssetPath(reader.GetString() ?? string.Empty);
        }

        public override void Write(Utf8JsonWriter writer, AssetPath value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.FullPath);
        }
    }
}


