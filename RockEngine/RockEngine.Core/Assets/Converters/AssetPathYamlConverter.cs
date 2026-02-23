using RockEngine.Assets;

using YamlDotNet.Core;

using YamlDotNet.Serialization;

namespace RockEngine.Core.Assets.Converters
{
    public class AssetPathYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(AssetPath);


        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Current as YamlDotNet.Core.Events.Scalar;
            parser.MoveNext();
            return new AssetPath(scalar?.Value ?? string.Empty);
        }


        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            var path = (AssetPath)value;
            emitter.Emit(new YamlDotNet.Core.Events.Scalar(path.ToString()));
        }
    }
}
