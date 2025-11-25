using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets.Converters
{
    public sealed class VertexConverter2 : JsonConverter<Vertex>
    {
        private const string Bitangent = "BG";
        private const string Tangent = "TG";
        private const string TexCoord = "T";
        private const string Normal = "N";
        private const string Position = "P";

        public override Vertex Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var obj = document.RootElement;

            return new Vertex
            {
                Position = obj.GetProperty(Position).Deserialize<Vector3>(options),
                Normal = obj.GetProperty(Normal).Deserialize<Vector3>(options),
                TexCoord = obj.GetProperty(TexCoord).Deserialize<Vector2>(options),
                Tangent = obj.GetProperty(Tangent).Deserialize<Vector3>(options),
                Bitangent = obj.GetProperty(Bitangent).Deserialize<Vector3>(options)
            };
        }

        public override void Write(Utf8JsonWriter writer, Vertex value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(Position);
            JsonSerializer.Serialize(writer, value.Position, options);
            writer.WritePropertyName(Normal);
            JsonSerializer.Serialize(writer, value.Normal, options);
            writer.WritePropertyName(TexCoord);
            JsonSerializer.Serialize(writer, value.TexCoord, options);
            writer.WritePropertyName(Tangent);
            JsonSerializer.Serialize(writer, value.Tangent, options);
            writer.WritePropertyName(Bitangent);
            JsonSerializer.Serialize(writer, value.Bitangent, options);
            writer.WriteEndObject();
        }
    }
}

