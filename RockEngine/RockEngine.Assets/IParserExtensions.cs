using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace RockEngine.Assets
{
    public static class IParserExtensions
    {
        extension(IParser parser)
        {
            public float ConsumeScalarAsFloat()
            {
                var scalar = parser.Consume<Scalar>();
                return float.Parse(scalar.Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}