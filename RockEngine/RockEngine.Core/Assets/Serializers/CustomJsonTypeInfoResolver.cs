using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using RockEngine.Core.Assets.Converters;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;

using System.Reflection;

namespace RockEngine.Core.Assets.Serializers
{
    public class CustomJsonTypeInfoResolver : DefaultJsonTypeInfoResolver
    {

        public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            JsonTypeInfo typeInfo = base.GetTypeInfo(type, options);

            if (typeInfo.Kind == JsonTypeInfoKind.Object)
            {
                // Only set CreateObject for concrete types, not interfaces or abstract classes
                if (!type.IsInterface && !type.IsAbstract)
                {
                    typeInfo.CreateObject = () =>
                    {
                        try
                        {
                            return IoC.Container.GetInstance(type);
                        }
                        catch
                        {
                            return Activator.CreateInstance(type);
                        }
                    };
                }

                // Modify properties based on custom attributes
                foreach (var property in typeInfo.Properties)
                {
                    var memberInfo = GetMemberInfo(type, property.Name);
                    if (memberInfo != null)
                    {
                        ApplyCustomAttributes(property, memberInfo, options);
                    }
                }

                // Add private fields with [Serialize] attribute
                AddSerializablePrivateFields(typeInfo, type, options);
            }

            return typeInfo;
        }

        private MemberInfo GetMemberInfo(Type type, string propertyName)
        {
            // Try to find property first
            var property = type.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null) return property;

            // Try to find field
            var field = type.GetField(propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field;
        }

        private void ApplyCustomAttributes(JsonPropertyInfo property, MemberInfo memberInfo, JsonSerializerOptions options)
        {
            // Check for SerializeIgnore attribute
            if (memberInfo.GetCustomAttribute<SerializeIgnoreAttribute>() != null)
            {
                property.ShouldSerialize = (obj, value) => false;
                return;
            }

            // Check for SerializeOrder attribute
            var orderAttr = memberInfo.GetCustomAttribute<SerializeOrderAttribute>();
            if (orderAttr != null)
            {
                property.Order = orderAttr.Order;
            }

            // Check for SerializeWith attribute
            var converterAttr = memberInfo.GetCustomAttribute<SerializeWithAttribute>();
            if (converterAttr != null)
            {
                var converter = (ISerializationConverter)IoC.Container.GetInstance(converterAttr.ConverterType);
                property.CustomConverter = CreateCustomJsonConverter(converter, property.PropertyType);
            }
        }

        private JsonConverter CreateCustomJsonConverter(ISerializationConverter converter, Type targetType)
        {
            // Use reflection to create the generic converter
            var converterType = typeof(CustomJsonConverterWrapper<>).MakeGenericType(targetType);
            return (JsonConverter)Activator.CreateInstance(converterType, converter);
        }

        private void AddSerializablePrivateFields(JsonTypeInfo typeInfo, Type type, JsonSerializerOptions options)
        {
            var privateFields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                   .Where(f => f.GetCustomAttribute<SerializeAttribute>() != null);

            foreach (var field in privateFields)
            {
                // Check if field is already included
                if (typeInfo.Properties.All(p => p.Name != field.Name))
                {
                    var fieldInfo = typeInfo.CreateJsonPropertyInfo(field.FieldType, field.Name);
                    fieldInfo.Get = field.GetValue;
                    fieldInfo.Set = field.SetValue;

                    // Apply custom attributes to the field
                    ApplyCustomAttributes(fieldInfo, field, options);

                    typeInfo.Properties.Add(fieldInfo);
                }
            }
        }
    }

    // Generic wrapper to convert ISerializationConverter to JsonConverter<T>
    public class CustomJsonConverterWrapper<T> : JsonConverter<T>
    {
        private readonly ISerializationConverter _converter;

        public CustomJsonConverterWrapper(ISerializationConverter converter)
        {
            _converter = converter;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var value = document.RootElement.Clone();
            return (T)_converter.ConvertFromSerializable(value);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var convertedValue = _converter.ConvertToSerializable(value);
            JsonSerializer.Serialize(writer, convertedValue, options);
        }
    }
}