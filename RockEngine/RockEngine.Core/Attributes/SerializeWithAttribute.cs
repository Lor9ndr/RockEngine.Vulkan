using RockEngine.Core.Assets.Converters;

namespace RockEngine.Core.Attributes
{
    /// <summary>
    /// Specifies a custom converter for serialization (both JSON and binary)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class SerializeWithAttribute : Attribute
    {
        public Type ConverterType { get; }

        public SerializeWithAttribute(Type converterType)
        {
            if (!typeof(ISerializationConverter).IsAssignableFrom(converterType))
                throw new ArgumentException("Converter type must implement ISerializationConverter");

            ConverterType = converterType;
        }
    }
}
