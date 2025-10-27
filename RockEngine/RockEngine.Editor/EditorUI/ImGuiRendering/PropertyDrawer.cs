using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers;

using System.Numerics;
using System.Reflection;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public class PropertyDrawer
    {
        private readonly AssetManager _assetManager;
        private readonly ImGuiController _imGuiController;
        private readonly Dictionary<Type, IReadOnlyList<UIPropertyAccessor>> _propertyCache;
        private readonly Dictionary<Type, IPropertyHandler> _propertyHandlers;

        public AssetManager AssetManager => _assetManager;
        public ImGuiController ImGuiController => _imGuiController;

        public PropertyDrawer(AssetManager assetManager, ImGuiController imGuiController)
        {
            _assetManager = assetManager;
            _imGuiController = imGuiController;
            _propertyCache = new Dictionary<Type, IReadOnlyList<UIPropertyAccessor>>();
            _propertyHandlers = new Dictionary<Type, IPropertyHandler>();
            InitializeHandlers();
        }

        private void InitializeHandlers()
        {
            // Discover and register all handlers via reflection
            var handlerTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(IPropertyHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var handlerType in handlerTypes)
            {
                var handler = Activator.CreateInstance(handlerType) as IPropertyHandler;
                var attr = handlerType.GetCustomAttribute<PropertyHandlerAttribute>();

                if (attr != null)
                {
                    foreach (var handledType in attr.HandledTypes)
                    {
                        _propertyHandlers[handledType] = handler;
                    }
                }
            }
        }

        public async ValueTask DrawComponentProperties(IComponent component)
        {
            var accessors = GetPropertyAccessors(component.GetType());

            foreach (var accessor in accessors)
            {
                await DrawProperty(component, accessor);
            }
        }

        public async ValueTask DrawProperty(IComponent component, UIPropertyAccessor accessor)
        {
            ImGui.PushID($"{component.GetType().Name}_{accessor.Name}");

            if (!accessor.CanWrite)
            {
                ImGui.BeginDisabled();
            }

            try
            {
                var value = accessor.GetValue(component);
                var handler = FindHandler(accessor.PropertyType);

                if (handler != null)
                {
                    await handler.Draw(component, accessor, value, this);
                }
                else
                {
                    // Fallback for unhandled types
                    ImGui.Text($"{accessor.DisplayName}: {value}");
                }
            }
            finally
            {
                if (!accessor.CanWrite)
                {
                    ImGui.EndDisabled();
                }

                ImGui.PopID();
            }
        }

        private IReadOnlyList<UIPropertyAccessor> GetPropertyAccessors(Type componentType)
        {
            if (!_propertyCache.TryGetValue(componentType, out var accessors))
            {
                // Try to use the generated method first
                var method = componentType.GetMethod("GetUIPropertyAccessors",
                    BindingFlags.Public | BindingFlags.Static);

                if (method != null)
                {
                    accessors = (IReadOnlyList<UIPropertyAccessor>)method.Invoke(null, null);
                }
                else
                {
                    // Fallback to reflection for non-generated types
                    accessors = CreateAccessorsViaReflection(componentType);
                }

                _propertyCache[componentType] = accessors;
            }

            return accessors;
        }

        private IReadOnlyList<UIPropertyAccessor> CreateAccessorsViaReflection(Type componentType)
        {
            var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead &&
                           !p.GetCustomAttributes<SerializeIgnoreAttribute>().Any() &&
                           p.GetMethod != null);

            var accessors = new List<UIPropertyAccessor>();

            foreach (var property in properties)
            {
                var uiAttr = property.GetCustomAttribute<UIEditableAttribute>();
                var displayName = uiAttr?.DisplayName ?? property.Name;

                var getter = CreateGetterDelegate(componentType, property);
                var setter = property.CanWrite ? CreateSetterDelegate(componentType, property) : null;
                var attributes = property.GetCustomAttributes().ToArray();

                var accessor = new UIPropertyAccessor(
                    property.Name,
                    displayName,
                    property.PropertyType,
                    getter,
                    setter,
                    property.CanWrite,
                    attributes
                );

                accessors.Add(accessor);
            }

            return accessors;
        }

        private PropertyGetter CreateGetterDelegate(Type componentType, PropertyInfo property)
        {
            var componentParam = System.Linq.Expressions.Expression.Parameter(typeof(IComponent), "component");
            var castComponent = System.Linq.Expressions.Expression.Convert(componentParam, componentType);
            var propertyAccess = System.Linq.Expressions.Expression.Property(castComponent, property);
            var castResult = System.Linq.Expressions.Expression.Convert(propertyAccess, typeof(object));

            var lambda = System.Linq.Expressions.Expression.Lambda<PropertyGetter>(castResult, componentParam);
            return lambda.Compile();
        }

        private PropertySetter CreateSetterDelegate(Type componentType, PropertyInfo property)
        {
            var componentParam = System.Linq.Expressions.Expression.Parameter(typeof(IComponent), "component");
            var valueParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "value");

            var castComponent = System.Linq.Expressions.Expression.Convert(componentParam, componentType);
            var castValue = System.Linq.Expressions.Expression.Convert(valueParam, property.PropertyType);
            var propertyAccess = System.Linq.Expressions.Expression.Property(castComponent, property);
            var assign = System.Linq.Expressions.Expression.Assign(propertyAccess, castValue);

            var lambda = System.Linq.Expressions.Expression.Lambda<PropertySetter>(assign, componentParam, valueParam);
            return lambda.Compile();
        }

        private IPropertyHandler FindHandler(Type propertyType)
        {
            // Exact type match
            if (_propertyHandlers.TryGetValue(propertyType, out var handler))
            {
                return handler;
            }

            // Generic type match (like AssetReference<>)
            if (propertyType.IsGenericType)
            {
                var genericType = propertyType.GetGenericTypeDefinition();
                if (_propertyHandlers.TryGetValue(genericType, out handler))
                {
                    return handler;
                }
            }

            // Check if the type implements any handled interfaces
            foreach (var interfaceType in propertyType.GetInterfaces())
            {
                if (_propertyHandlers.TryGetValue(interfaceType, out handler))
                {
                    return handler;
                }

                // Check for generic interfaces
                if (interfaceType.IsGenericType)
                {
                    var genericInterface = interfaceType.GetGenericTypeDefinition();
                    if (_propertyHandlers.TryGetValue(genericInterface, out handler))
                    {
                        return handler;
                    }
                }
            }

            // Base type match (like Enum)
            foreach (var kvp in _propertyHandlers)
            {
                if (kvp.Key.IsAssignableFrom(propertyType))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        public void HandleTexturePreview(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            nint texId = _imGuiController.GetTextureID(texture);
            float previewWidth = Math.Min(ImGui.GetContentRegionAvail().X, 200);
            Vector2 previewSize = new Vector2(previewWidth, previewWidth * (texture.Height / (float)texture.Width));

            ImGui.Image(texId, previewSize);
            ImGui.Text($"Resolution: {texture.Width}x{texture.Height}");
            ImGui.Text($"Mip Levels: {texture.LoadedMipLevels}/{texture.TotalMipLevels}");
        }

        public void CreateNewMaterialForProperty(IComponent component, UIPropertyAccessor accessor)
        {
            try
            {
                // Your material creation logic here
                // This could create a new material asset and assign it to the property
            }
            catch (Exception ex)
            {
                // Log error
            }
        }
    }
}