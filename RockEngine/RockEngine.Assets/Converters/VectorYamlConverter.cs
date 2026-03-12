using System.Numerics;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace RockEngine.Assets.Converters
{
    public class VectorYamlConverter : IYamlTypeConverter
    {
        private static readonly Type[] _supportedTypes = new[]
        {
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion)
        };

        public bool Accepts(Type type) => Array.IndexOf(_supportedTypes, type) >= 0;

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            // Expect a sequence of floats
            parser.Consume<SequenceStart>();
            float x = parser.ConsumeScalarAsFloat();
            float y = parser.ConsumeScalarAsFloat();

            if (type == typeof(Vector2))
            {
                parser.Consume<SequenceEnd>();
                return new Vector2(x, y);
            }

            float z = parser.ConsumeScalarAsFloat();
            if (type == typeof(Vector3))
            {
                parser.Consume<SequenceEnd>();
                return new Vector3(x, y, z);
            }
            float w = parser.ConsumeScalarAsFloat();

            if (type == typeof(Vector4))
            {
                parser.Consume<SequenceEnd>();
                return new Vector4(x, y, z, w);
            }
            else
            {
                parser.Consume<SequenceEnd>();
                return new Quaternion(x, y, z, w);
            }
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            if (value == null)
            {
                emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, false, SequenceStyle.Block));
                emitter.Emit(new SequenceEnd());
                return;
            }

            emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, false, SequenceStyle.Flow));

            switch (value)
            {
                case Vector2 v2:
                    emitter.Emit(new Scalar(v2.X.ToString("R")));
                    emitter.Emit(new Scalar(v2.Y.ToString("R")));
                    break;
                case Vector3 v3:
                    emitter.Emit(new Scalar(v3.X.ToString("R")));
                    emitter.Emit(new Scalar(v3.Y.ToString("R")));
                    emitter.Emit(new Scalar(v3.Z.ToString("R")));
                    break;
                case Vector4 v4:
                    emitter.Emit(new Scalar(v4.X.ToString("R")));
                    emitter.Emit(new Scalar(v4.Y.ToString("R")));
                    emitter.Emit(new Scalar(v4.Z.ToString("R")));
                    emitter.Emit(new Scalar(v4.W.ToString("R")));
                    break;
                case Quaternion q:
                    emitter.Emit(new Scalar(q.X.ToString("R")));
                    emitter.Emit(new Scalar(q.Y.ToString("R")));
                    emitter.Emit(new Scalar(q.Z.ToString("R")));
                    emitter.Emit(new Scalar(q.W.ToString("R")));
                    break;
            }

            emitter.Emit(new SequenceEnd());
        }
    }
}