using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets.Converters
{
    /// <summary>
    /// Mimics Newtonsoft.Json's TypeNameHandling.Objects behavior
    /// </summary>
    public class TypeHandlingConverter : JsonConverter<object>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            // Handle all object types except primitives
            return typeToConvert.IsClass && typeToConvert != typeof(string);
        }

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            // For objects with type information
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var root = document.RootElement;

                // Check if this object has type information
                if (root.TryGetProperty("$type", out var typeElement))
                {
                    var typeName = typeElement.GetString();
                    var actualType = Type.GetType(typeName);

                    if (actualType != null)
                    {
                        // Remove $type property and deserialize the rest
                        var jsonWithoutType = RemoveTypeProperty(root);
                        
                        // Create options without this converter to prevent recursion
                        var optionsWithoutConverter = CreateOptionsWithoutConverter(options);
                        return JsonSerializer.Deserialize(jsonWithoutType, actualType, optionsWithoutConverter);
                    }
                }

                // Fallback: deserialize without type info using options without converter
                var fallbackOptions = CreateOptionsWithoutConverter(options);
                return JsonSerializer.Deserialize(root.GetRawText(), typeToConvert, fallbackOptions);
            }

            // For simple values, use options without converter
            var simpleOptions = CreateOptionsWithoutConverter(options);
            return JsonSerializer.Deserialize(ref reader, typeToConvert, simpleOptions);
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            var valueType = value.GetType();

            // Don't add type info for simple types
            if (IsSimpleType(valueType))
            {
                // Use options without converter for simple types
                var simpleOptions = CreateOptionsWithoutConverter(options);
                JsonSerializer.Serialize(writer, value, valueType, simpleOptions);
                return;
            }

            // For complex types, add type information
            writer.WriteStartObject();
            writer.WriteString("$type", valueType.AssemblyQualifiedName);

            // Serialize all properties except ones we're handling specially
            // Only include properties that can be read and don't have parameters (exclude indexers)
            var properties = valueType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && 
                           p.Name != "$type" && 
                           p.GetIndexParameters().Length == 0 &&  // Exclude indexers
                           !IsIgnoredProperty(p))                  // Check for ignore attributes
                .ToList();

            foreach (var property in properties)
            {
                // Skip properties with JsonIgnore attribute
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                    continue;

                try
                {
                    var propertyValue = property.GetValue(value);
                    if (options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull && propertyValue == null)
                        continue;

                    writer.WritePropertyName(GetPropertyName(property, options));
                    
                    // Use options without converter for property values to prevent recursion
                    var propertyOptions = CreateOptionsWithoutConverter(options);
                    JsonSerializer.Serialize(writer, propertyValue, property.PropertyType, propertyOptions);
                }
                catch (TargetParameterCountException)
                {
                    // Skip indexer properties that slipped through the filter
                    continue;
                }
            }

            writer.WriteEndObject();
        }

        private static string GetPropertyName(PropertyInfo property, JsonSerializerOptions options)
        {
            var jsonPropertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jsonPropertyName != null)
                return jsonPropertyName.Name;

            return options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
        }

        private static JsonElement RemoveTypeProperty(JsonElement element)
        {
            var newJson = new Dictionary<string, JsonElement>();
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name != "$type")
                {
                    newJson[property.Name] = property.Value.Clone();
                }
            }
            return JsonSerializer.SerializeToElement(newJson);
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(TimeSpan) ||
                   type == typeof(Guid) ||
                   type.IsEnum ||
                   (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
        }

        private static bool IsIgnoredProperty(PropertyInfo property)
        {
            return property.GetCustomAttribute<JsonIgnoreAttribute>() != null;
        }

        /// <summary>
        /// Creates a new JsonSerializerOptions without the TypeHandlingConverter to prevent recursion
        /// </summary>
        private JsonSerializerOptions CreateOptionsWithoutConverter(JsonSerializerOptions originalOptions)
        {
            var newOptions = new JsonSerializerOptions(originalOptions);
            
            // Remove this converter type from the converters list
            newOptions.Converters.RemoveAll< TypeHandlingConverter>();
            
            return newOptions;
        }
    }
    public static class JsonSerializerOptionsExtensions
    {
        public static void RemoveAll<T>(this IList<JsonConverter> converters)
        {
            for (int i = converters.Count - 1; i >= 0; i--)
            {
                if (converters[i] is T)
                {
                    converters.RemoveAt(i);
                }
            }
        }
    }
}