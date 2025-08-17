using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RockEngine.Core;

using System.Numerics;

namespace RockEngine.Core.Assets.Converters
{
    public sealed class VertexConverter : JsonConverter<Vertex>
    {
        private const string _bitanget = "BG";
        private const string _tangent = "TG";
        private const string _texCoord = "T";
        private const string _normal = "N";
        private const string _position = "P";

        public override Vertex ReadJson(JsonReader reader, Type objectType, Vertex existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            return new Vertex
            {
                Position = obj[_position].ToObject<Vector3>(),
                Normal = obj[_normal].ToObject<Vector3>(),
                TexCoord = obj[_texCoord].ToObject<Vector2>(),
                Tangent = obj[_tangent].ToObject<Vector3>(),
                Bitangent = obj[_bitanget].ToObject<Vector3>()
            };
        }

        public override void WriteJson(JsonWriter writer, Vertex value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(_position);
            serializer.Serialize(writer, value.Position);
            writer.WritePropertyName(_normal);
            serializer.Serialize(writer, value.Normal);
            writer.WritePropertyName(_texCoord);
            serializer.Serialize(writer, value.TexCoord);
            writer.WritePropertyName(_tangent);
            serializer.Serialize(writer, value.Tangent);
            writer.WritePropertyName(_bitanget);
            serializer.Serialize(writer, value.Bitangent);
            writer.WriteEndObject();
        }
    }
}