using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RockEngine.Core.Assets.Converters
{
    public class AutomaticTypeResolver : DefaultJsonTypeInfoResolver
    {
        private readonly HashSet<Type> _processedTypes = new();

        public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            JsonTypeInfo typeInfo = base.GetTypeInfo(type, options);

            if (ShouldAddPolymorphicHandling(type) && !_processedTypes.Contains(type))
            {
                _processedTypes.Add(type);
                typeInfo.PolymorphismOptions = CreatePolymorphicOptions(type);
            }

            return typeInfo;
        }

        private JsonPolymorphismOptions CreatePolymorphicOptions(Type baseType)
        {
            var options = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
            };

            if (baseType.IsInterface || baseType.IsAbstract)
            {
                // Discover derived types automatically
                var derivedTypes = DiscoverDerivedTypes(baseType);
                foreach (var derivedType in derivedTypes)
                {
                    options.DerivedTypes.Add(new JsonDerivedType(derivedType, derivedType.FullName));
                }
            }
            else
            {
                options.DerivedTypes.Add(new JsonDerivedType(baseType, baseType.FullName));
            }

            return options;
        }

        private static IEnumerable<Type> DiscoverDerivedTypes(Type baseType)
        {
            // Look in all loaded assemblies for types that inherit from/implement baseType
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch
                    {
                        return Array.Empty<Type>();
                    }
                })
                .Where(type => baseType.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                .Take(100); // Limit to prevent performance issues
        }

        private static bool ShouldAddPolymorphicHandling(Type type)
        {
            return !type.IsPrimitive &&
                   type != typeof(string) &&
                   !type.IsEnum &&
                   !type.IsSealed &&
                   type != typeof(object) &&
                   !type.IsArray &&
                   (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Nullable<>));
        }
    }

}

