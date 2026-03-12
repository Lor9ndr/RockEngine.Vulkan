using YamlDotNet.Core;

using YamlDotNet.Serialization;

namespace RockEngine.Core.Assets.Converters
{
    public class AssetReferenceYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AssetReference<>);


        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Current as YamlDotNet.Core.Events.Scalar;
            parser.MoveNext();

            if (Guid.TryParse(scalar?.Value, out var guid))
            {
                var referenceType = typeof(AssetReference<>).MakeGenericType(type.GetGenericArguments()[0]);
                return Activator.CreateInstance(referenceType, guid);
            }

            return null;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            var idProperty = type.GetProperty("AssetID");
            var guid = (Guid)idProperty.GetValue(value);
            emitter.Emit(new YamlDotNet.Core.Events.Scalar(guid.ToString()));
        }
    }
}
