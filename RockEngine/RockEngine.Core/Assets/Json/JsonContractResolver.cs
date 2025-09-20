using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using RockEngine.Core.Assets.Converters;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;

using System.Reflection;

namespace RockEngine.Core.Assets.Json
{
    public class JsonContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            // Check for SerializeIgnore attribute
            if (member.GetCustomAttribute<SerializeIgnoreAttribute>() != null)
            {
                property.Ignored = true;
                return property;
            }

            // Check for SerializeOrder attribute
            var orderAttr = member.GetCustomAttribute<SerializeOrderAttribute>();
            if (orderAttr != null)
            {
                property.Order = orderAttr.Order;
            }

            // Check for SerializeWith attribute
            var converterAttr = member.GetCustomAttribute<SerializeWithAttribute>();
            if (converterAttr != null)
            {
                var converter = (ISerializationConverter)IoC.Container.GetInstance(converterAttr.ConverterType);
                property.Converter = new CustomJsonConverterWrapper(converter);
            }

            return property;
        }

        private class CustomJsonConverterWrapper : JsonConverter
        {
            private readonly ISerializationConverter _converter;

            public CustomJsonConverterWrapper(ISerializationConverter converter)
            {
                _converter = converter;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var convertedValue = _converter.ConvertToSerializable(value);
                serializer.Serialize(writer, convertedValue);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var value = serializer.Deserialize(reader);
                return _converter.ConvertFromSerializable(value);
            }

            public override bool CanConvert(Type objectType)
            {
                return true;
            }
        }
    }
}
