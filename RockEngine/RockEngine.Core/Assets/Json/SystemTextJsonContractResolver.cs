using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Reflection;
using System.Runtime.CompilerServices;
using RockEngine.Core.Assets.Converters;
using RockEngine.Core.Attributes;
using SimpleInjector;

namespace RockEngine.Core.Assets.Json
{
    public class SystemTextJsonContractResolver : DefaultJsonTypeInfoResolver
    {
        private readonly IServiceProvider _container;

        public SystemTextJsonContractResolver(IServiceProvider container)
        {
            _container = container;
        }

        public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            JsonTypeInfo typeInfo = base.GetTypeInfo(type, options);

            if (typeInfo.Kind == JsonTypeInfoKind.Object)
            {
                ProcessType(typeInfo, options);
            }

            return typeInfo;
        }

        private void ProcessType(JsonTypeInfo typeInfo, JsonSerializerOptions options)
        {
            var propertiesToRemove = new List<JsonPropertyInfo>();

            // Process existing properties
            foreach (var property in typeInfo.Properties)
            {
                if (ShouldIgnore(property, typeInfo.Type) || !IsValidForSerialization(property.PropertyType))
                {
                    propertiesToRemove.Add(property);
                }
                else
                {
                    ApplyPropertyAttributes(property, typeInfo.Type, options);
                }
            }

            // Remove invalid properties
            foreach (var property in propertiesToRemove)
            {
                typeInfo.Properties.Remove(property);
            }

            // Add serializable fields (only valid ones)
            AddSerializableFields(typeInfo, options);

            // Reorder properties
            ReorderProperties(typeInfo);
        }

        private bool ShouldIgnore(JsonPropertyInfo property, Type declaringType)
        {
            // Check if property has SerializeIgnore attribute
            var propertyInfo = declaringType.GetProperty(property.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo?.GetCustomAttribute<SerializeIgnoreAttribute>() != null)
                return true;

            // Check if property doesn't have Serialize attribute and is not public
            if (propertyInfo != null)
            {
                var hasSerializeAttr = propertyInfo.GetCustomAttribute<SerializeAttribute>() != null;
                var isPublic = propertyInfo.GetGetMethod()?.IsPublic == true || propertyInfo.GetSetMethod()?.IsPublic == true;

                // If it's not public and doesn't have Serialize attribute, ignore it
                if (!isPublic && !hasSerializeAttr)
                    return true;
            }

            return false;
        }

        private bool IsValidForSerialization(Type type)
        {
            // Check for pointer types
            if (type.IsPointer)
                return false;

            // Check for by-reference types (like Silk.NET.Vulkan.DescriptorSet&)
            if (type.IsByRef)
                return false;

            // Check for ref structs
            if (type.IsByRefLike)
                return false;

            // Check for function pointers
            if (type == typeof(IntPtr) || type == typeof(UIntPtr))
            {
                // Allow IntPtr/UIntPtr but log warning or handle specially if needed
                return true; // or false depending on your needs
            }

            // Check for open generic types
            if (type.ContainsGenericParameters && !type.IsGenericTypeDefinition)
                return false;


            // Check for specific problematic types from Silk.NET.Vulkan
            if ((type.Name.Contains('&') || type.IsByRef || type.IsPointer))
                return false;

            // Recursively check generic arguments
            if (type.IsGenericType)
            {
                foreach (var genericArg in type.GetGenericArguments())
                {
                    if (!IsValidForSerialization(genericArg))
                        return false;
                }
            }

            // Check array elements
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                var elementType = type.GetElementType();
                if (elementType != null && !IsValidForSerialization(elementType))
                    return false;
            }

            return true;
        }

        private bool IsUnmanagedType(Type type)
        {
            try
            {
      /*          // Try to determine if this is an unmanaged type that might cause issues
                if (type.IsPrimitive || type.IsEnum)
                    return true;*/

                if (type.IsValueType && !type.IsGenericType)
                {
                    // Check if all fields are unmanaged
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        if (!IsValidForSerialization(field.FieldType))
                            return true;
                    }
                    return false;
                }

                return false;
            }
            catch
            {
                // If we can't determine, assume it's problematic
                return true;
            }
        }

        private void ApplyPropertyAttributes(JsonPropertyInfo jsonProperty, Type declaringType, JsonSerializerOptions options)
        {
            var propertyInfo = declaringType.GetProperty(jsonProperty.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (propertyInfo != null)
            {
                // Apply order
                var orderAttr = propertyInfo.GetCustomAttribute<SerializeOrderAttribute>();
                if (orderAttr != null)
                {
                    jsonProperty.Order = orderAttr.Order;
                }

                // Apply converter
                var converterAttr = propertyInfo.GetCustomAttribute<SerializeWithAttribute>();
                if (converterAttr != null)
                {
                    var converter = (ISerializationConverter)_container.GetService(converterAttr.ConverterType);
                    if (converter != null)
                    {
                        jsonProperty.CustomConverter = CreateJsonConverter(propertyInfo.PropertyType, converter);
                    }
                }
            }
        }

        private void AddSerializableFields(JsonTypeInfo typeInfo, JsonSerializerOptions options)
        {
            var type = typeInfo.Type;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                // Skip if already has a property with the same name
                if (typeInfo.Properties.Any(p =>
                    string.Equals(p.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Skip invalid field types
                if (!IsValidForSerialization(field.FieldType))
                    continue;

                // Include public fields or fields with [Serialize] attribute
                var hasSerializeAttr = field.GetCustomAttribute<SerializeAttribute>() != null;
                var isPublic = field.IsPublic;

                if (!isPublic && !hasSerializeAttr)
                    continue;

                // Skip if field has [SerializeIgnore]
                if (field.GetCustomAttribute<SerializeIgnoreAttribute>() != null)
                    continue;

                var jsonPropertyInfo = typeInfo.CreateJsonPropertyInfo(
                    field.FieldType,
                    options.PropertyNamingPolicy?.ConvertName(field.Name) ?? field.Name);

                jsonPropertyInfo.Get = field.GetValue;
                jsonPropertyInfo.Set = field.SetValue;

                // Apply order attribute
                var orderAttr = field.GetCustomAttribute<SerializeOrderAttribute>();
                if (orderAttr != null)
                {
                    jsonPropertyInfo.Order = orderAttr.Order;
                }

                // Apply converter attribute
                var converterAttr = field.GetCustomAttribute<SerializeWithAttribute>();
                if (converterAttr != null)
                {
                    var converter = (ISerializationConverter)_container.GetService(converterAttr.ConverterType);
                    if (converter != null)
                    {
                        jsonPropertyInfo.CustomConverter = CreateJsonConverter(field.FieldType, converter);
                    }
                }

                typeInfo.Properties.Add(jsonPropertyInfo);
            }
        }

        private void ReorderProperties(JsonTypeInfo typeInfo)
        {
            var orderedProperties = typeInfo.Properties
                .OrderBy(p => p.Order)
                .ThenBy(p => p.Name)
                .ToList();

            typeInfo.Properties.Clear();
            foreach (var property in orderedProperties)
            {
                typeInfo.Properties.Add(property);
            }
        }

        private JsonConverter CreateJsonConverter(Type propertyType, ISerializationConverter converter)
        {
            var wrapperType = typeof(SystemTextJsonConverterWrapper<>).MakeGenericType(propertyType);
            return (JsonConverter)Activator.CreateInstance(wrapperType, converter);
        }
    }

    public class SystemTextJsonConverterWrapper<T> : JsonConverter<T>
    {
        private readonly ISerializationConverter _converter;

        public SystemTextJsonConverterWrapper(ISerializationConverter converter)
        {
            _converter = converter;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = JsonSerializer.Deserialize<object>(ref reader, options);
            return (T)_converter.ConvertFromSerializable(value);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var convertedValue = _converter.ConvertToSerializable(value);
            JsonSerializer.Serialize(writer, convertedValue, options);
        }
    }
}