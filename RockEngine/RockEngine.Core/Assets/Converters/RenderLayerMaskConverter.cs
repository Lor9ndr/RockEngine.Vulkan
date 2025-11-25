using RockEngine.Core.Rendering;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets.Converters
{
    public class RenderLayerMaskConverter2 : JsonConverter<RenderLayerMask>
    {
        public override RenderLayerMask Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                if (ulong.TryParse(stringValue, out ulong numericValue))
                {
                    return (RenderLayerMask)numericValue;
                }

                if (Enum.TryParse<RenderLayerMask>(stringValue, out var enumValue))
                {
                    return enumValue;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetUInt64(out ulong numericValue))
                {
                    return (RenderLayerMask)numericValue;
                }
            }

            return RenderLayerMask.None;
        }

        public override void Write(Utf8JsonWriter writer, RenderLayerMask value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue((ulong)value);
        }
    }
}

