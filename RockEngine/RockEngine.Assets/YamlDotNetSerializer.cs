using RockEngine.Assets.Converters;
using RockEngine.Core.Attributes;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RockEngine.Assets
{
    public class YamlDotNetSerializer : IYamlSerializer
    {
        private readonly ISerializer _serializer;
        private readonly IDeserializer _deserializer;

        public YamlDotNetSerializer()
        {
            var serializerBuilder = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeInspector(inner => new CustomTypeInspector(inner))
                .WithTypeConverter(new EnumAsNumericConverter())
                .DisableAliases()
                 .WithTypeConverter(new VectorYamlConverter())
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull);

            var deserializerBuilder = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeInspector(inner => new CustomTypeInspector(inner))
                .WithTypeConverter(new EnumAsNumericConverter())
                 .WithTypeConverter(new VectorYamlConverter())
                .IgnoreUnmatchedProperties();

            _serializer = serializerBuilder.Build();
            _deserializer = deserializerBuilder.Build();
        }

        public async Task SerializeAsync(object data, Stream stream)
        {
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            _serializer.Serialize(writer, data);
            await writer.FlushAsync();
        }

        public async Task<object> DeserializeAsync(Stream stream, Type type)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var content = await reader.ReadToEndAsync();
            return _deserializer.Deserialize(content, type);
        }

        public object DeserializeFromString(string yaml, Type type)
        {
            return _deserializer.Deserialize(yaml, type);
        }
    }

    /// <summary>
    /// Custom TypeInspector that respects our SerializeAttribute and SerializeIgnoreAttribute
    /// </summary>
    public class CustomTypeInspector : ITypeInspector
    {
        private readonly ITypeInspector _innerInspector;

        public CustomTypeInspector(ITypeInspector innerInspector)
        {
            _innerInspector = innerInspector;
        }

        public IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container)
        {
            var descriptors = new List<IPropertyDescriptor>();

            // Get public properties - исключаем индексаторы (свойства с параметрами)
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Where(p => p.GetIndexParameters().Length == 0) // Исключаем индексаторы
                .Where(p => !p.IsDefined(typeof(SerializeIgnoreAttribute), true))
                .Select(p => new PropertyDescriptorWrapper(p, _innerInspector))
                .ToList<IPropertyDescriptor>();

            // Get fields with Serialize attribute
            var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                .Where(f => f.IsDefined(typeof(SerializeAttribute), true))
                .Where(f => !f.IsDefined(typeof(SerializeIgnoreAttribute), true))
                .Select(f => new FieldDescriptorWrapper(f))
                .ToList<IPropertyDescriptor>();

            descriptors.AddRange(properties);
            descriptors.AddRange(fields);

            // Sort by order
            return descriptors.OrderBy(d => GetOrder(d));
        }

        public IPropertyDescriptor? GetProperty(Type type, object? container, string name, [MaybeNullWhen(true)] bool ignoreUnmatched, bool caseInsensitivePropertyMatching)
        {
            // Получаем все свойства
            var properties = GetProperties(type, container).ToList();

            StringComparison comparison = caseInsensitivePropertyMatching
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // Ищем свойство по имени
            var descriptor = properties.FirstOrDefault(p =>
                string.Equals(p.Name, name, comparison));

            if (descriptor != null)
                return descriptor;

            // Если свойство не найдено и не нужно игнорировать, выбрасываем исключение
            if (!ignoreUnmatched)
                throw new InvalidOperationException($"Property '{name}' not found on type '{type.Name}'");

            // Возвращаем null, если свойство не найдено и нужно игнорировать
            return null;
        }

        public string GetEnumName(Type enumType, string name)
        {
            if (string.IsNullOrEmpty(name))
                return "0";

            try
            {
                // Проверяем, существует ли такое значение в перечислении
                if (Enum.IsDefined(enumType, name))
                    return name;

                // Пробуем найти по числовому значению
                if (int.TryParse(name, out int intValue) && Enum.IsDefined(enumType, intValue))
                    return Enum.GetName(enumType, intValue);

                return "0";
            }
            catch
            {
                return "0";
            }
        }

        public string GetEnumValue(object value)
        {
            if (value is null)
                return "0";

            try
            {
                var underlyingType = Enum.GetUnderlyingType(value.GetType());
                return Convert.ChangeType(value, underlyingType).ToString() ?? "0";
            }
            catch
            {
                return value.ToString() ?? "0";
            }
        }

        private int GetOrder(IPropertyDescriptor descriptor)
        {
            if (descriptor is PropertyDescriptorWrapper propWrapper)
            {
                var orderAttr = propWrapper.PropertyInfo.GetCustomAttribute<SerializeOrderAttribute>();
                return orderAttr?.Order ?? 1000;
            }
            else if (descriptor is FieldDescriptorWrapper fieldWrapper)
            {
                var orderAttr = fieldWrapper.FieldInfo.GetCustomAttribute<SerializeOrderAttribute>();
                return orderAttr?.Order ?? 1000;
            }
            return 1000;
        }
    }

    /// <summary>
    /// Wrapper for PropertyInfo that implements IPropertyDescriptor
    /// </summary>
    public class PropertyDescriptorWrapper : IPropertyDescriptor
    {
        public PropertyInfo PropertyInfo { get; }

        public PropertyDescriptorWrapper(PropertyInfo propertyInfo, ITypeInspector inspector)
        {
            PropertyInfo = propertyInfo;
        }

        public string Name => PropertyInfo.Name;
        public bool CanWrite => PropertyInfo.CanWrite;
        public Type Type => PropertyInfo.PropertyType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; }
        public ScalarStyle ScalarStyle { get; set; }
        public bool AllowNulls { get; set; } = true;
        public bool Required { get; set; } = false;
        public Type? ConverterType { get; set; }

        public T? GetCustomAttribute<T>() where T : Attribute
        {
            return PropertyInfo.GetCustomAttribute<T>();
        }

        public IObjectDescriptor Read(object target)
        {
            try
            {
                // Проверяем, что свойство не требует параметров
                if (PropertyInfo.GetIndexParameters().Length > 0)
                {
                    // Это индексатор - пропускаем
                    return new ObjectDescriptor(null, Type, Type);
                }

                var value = PropertyInfo.GetValue(target);
                return new ObjectDescriptor(value, Type, Type);
            }
            catch (TargetParameterCountException ex)
            {
                // Логируем ошибку и возвращаем null
                System.Diagnostics.Debug.WriteLine($"Error reading property {PropertyInfo.Name}: {ex.Message}");
                return new ObjectDescriptor(null, Type, Type);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading property {PropertyInfo.Name}: {ex.Message}");
                return new ObjectDescriptor(null, Type, Type);
            }
        }

        public void Write(object target, object? value)
        {
            if (CanWrite)
            {
                try
                {
                    PropertyInfo.SetValue(target, value);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to set property {PropertyInfo.Name}", ex);
                }
            }
        }
    }

    /// <summary>
    /// Wrapper for FieldInfo that implements IPropertyDescriptor
    /// </summary>
    public class FieldDescriptorWrapper : IPropertyDescriptor
    {
        public FieldInfo FieldInfo { get; }

        public FieldDescriptorWrapper(FieldInfo fieldInfo)
        {
            FieldInfo = fieldInfo;
        }

        public string Name => FieldInfo.Name;
        public bool CanWrite => !FieldInfo.IsInitOnly;
        public Type Type => FieldInfo.FieldType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; }
        public ScalarStyle ScalarStyle { get; set; }
        public bool AllowNulls { get; set; } = true;
        public bool Required { get; set; } = false;
        public Type? ConverterType { get; set; }

        public T? GetCustomAttribute<T>() where T : Attribute
        {
            return FieldInfo.GetCustomAttribute<T>();
        }

        public IObjectDescriptor Read(object target)
        {
            try
            {
                var value = FieldInfo.GetValue(target);
                return new ObjectDescriptor(value, Type, Type);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading field {FieldInfo.Name}: {ex.Message}");
                return new ObjectDescriptor(null, Type, Type);
            }
        }

        public void Write(object target, object? value)
        {
            if (CanWrite)
            {
                try
                {
                    FieldInfo.SetValue(target, value);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to set field {FieldInfo.Name}", ex);
                }
            }
        }
    }

    public class EnumAsNumericConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type.IsEnum;

        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            // Если текущий узел — не скаляр, возвращаем default(enum)
            if (!parser.TryConsume<Scalar>(out var scalar))
            {
                return Activator.CreateInstance(type);
            }

            var value = scalar.Value;

            // Пустая строка или null → default(enum)
            if (string.IsNullOrEmpty(value))
            {
                return Activator.CreateInstance(type);
            }

            // Пытаемся преобразовать строку в число с учётом базового типа enum
            var underlyingType = Enum.GetUnderlyingType(type);
            try
            {
                var numericValue = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
                return Enum.ToObject(type, numericValue);
            }
            catch
            {
                // Если не число — всё равно пытаемся преобразовать через long
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                {
                    return Enum.ToObject(type, longValue);
                }

                // Если совсем ничего — default
                return Activator.CreateInstance(type);
            }
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            if (value == null)
            {
                emitter.Emit(new Scalar("0"));
                return;
            }

            // Приводим enum к его базовому целочисленному типу и выводим как строку числа
            var underlyingType = Enum.GetUnderlyingType(type);
            var numericValue = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
            emitter.Emit(new Scalar(numericValue.ToString()!));
        }
    }
}