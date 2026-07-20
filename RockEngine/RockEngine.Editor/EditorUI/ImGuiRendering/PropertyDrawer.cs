using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using ImGuiNET;
using RockEngine.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers;
using RockEngine.Editor.EditorUI.Thumbnails;
using RockEngine.Editor.Generator;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public class PropertyDrawer
    {
        private readonly IAssetManager _assetManager;
        private readonly ImGuiController _imGuiController;
        private readonly Dictionary<Type, IReadOnlyList<UIPropertyAccessor>> _componentAccessorCache = new();
        private readonly Dictionary<Type, IReadOnlyList<UIPropertyAccessor>> _generalAccessorCache = new();
        private readonly Dictionary<Type, IPropertyHandler> _propertyHandlers;

        public IAssetManager AssetManager => _assetManager;
        public ImGuiController ImGuiController => _imGuiController;
        public IThumbnailService ThumbnailService { get; }

        // Optional callback for sampler state changes
        public System.Action<SamplerState>? OnSamplerStateChanged;

        public PropertyDrawer(IAssetManager assetManager, ImGuiController imGuiController, IThumbnailService thumbnailService)
        {
            _assetManager = assetManager;
            _imGuiController = imGuiController;
            ThumbnailService = thumbnailService;

            // AOT‑safe: handler dictionary is generated at compile time
            _propertyHandlers = new Dictionary<Type, IPropertyHandler>(HandlerRegistry.GetAllHandlers());
        }

        // Draw all properties of a component
        public void DrawComponentProperties(IComponent component)
        {
            IReadOnlyList<UIPropertyAccessor> accessors;

            // 1) Свой компонент – интерфейс прямо на объекте
            if (component is IUIPropertyAccessorProvider provider)
            {
                accessors = provider.GetUIPropertyAccessors();
            }
            else
            {
                // 2) Зависимый компонент – фабрика, сгенерированная компилятором
                var wrapper = UIPropertyAccessorProviderFactory.GetProvider(component);
                if (wrapper == null)
                {
                    throw new InvalidOperationException($"No accessor provider for {component.GetType().FullName}");
                }

                accessors = wrapper.GetUIPropertyAccessors();
            }

            foreach (var accessor in accessors)
            {
                DrawProperty(component, accessor);
            }
        }
        // Draw properties of any object given an accessor list
        public void DrawObjectProperties(object target, IReadOnlyList<UIPropertyAccessor> accessors)
        {
            foreach (var accessor in accessors)
            {
                DrawProperty(target, accessor);
            }
        }

        // Draw a single property
        public void DrawProperty(object owner, UIPropertyAccessor accessor)
        {
            if (!accessor.CanWrite)
            {
                ImGui.BeginDisabled();
            }

            ImGui.PushID($"{owner.GetType().Name}_{accessor.Name}");

            try
            {
                var value = accessor.GetValue((object)owner);
                var handler = FindHandler(accessor.PropertyType);

                if (handler != null)
                {
                    handler.Draw(owner, accessor, value, this);
                }
                else
                {
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

        // Get accessors for a component type (cached, uses generated method)
        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetType(String)")]
        private IReadOnlyList<UIPropertyAccessor> GetComponentAccessors([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type componentType)
        {
            if (_componentAccessorCache.TryGetValue(componentType, out var accessors))
            {
                return accessors;
            }

            // 1) Try own-project pattern: method directly on the component type
            var method = componentType.GetMethod("GetUIPropertyAccessors",
                BindingFlags.Public | BindingFlags.Static);
            if (method != null)
            {
                accessors = (IReadOnlyList<UIPropertyAccessor>)method.Invoke(null, null)!;
                _componentAccessorCache[componentType] = accessors;
                return accessors;
            }

            // 2) Fallback: dependency pattern – search in the Editor assembly
            // The source generator emits the helper class in the same project as PropertyDrawer.
            var helperTypeName = $"{componentType.FullName}UIProperties";
            var editorAssembly = typeof(PropertyDrawer).Assembly;
            var helperType = editorAssembly.GetType(helperTypeName);

            if (helperType == null)
            {
                throw new InvalidOperationException(
                    $"Missing generated accessors for {componentType.FullName}. " +
                    $"Neither a direct method nor a helper class '{helperTypeName}' was found " +
                    $"in the Editor assembly. Ensure the source generator ran successfully.");
            }

            method = helperType.GetMethod("GetUIPropertyAccessors",
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"Helper class '{helperTypeName}' is missing the static GetUIPropertyAccessors method.");

            accessors = (IReadOnlyList<UIPropertyAccessor>)method.Invoke(null, null)!;
            _componentAccessorCache[componentType] = accessors;
            return accessors;
        }
        // Get accessors for any type with [GenerateUIProperties] (cached, uses generated method)
        public IReadOnlyList<UIPropertyAccessor> GetAccessorsForType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] System.Type type)
        {
            if (!_generalAccessorCache.TryGetValue(type, out var accessors))
            {
                var method = type.GetMethod("GetUIPropertyAccessors", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    throw new InvalidOperationException(
                        $"Type '{type.FullName}' has no generated UI property accessors. " +
                        "Apply [GenerateUIProperties] or ensure the source generator ran.");
                }

                accessors = (IReadOnlyList<UIPropertyAccessor>)method.Invoke(null, null)!;
                _generalAccessorCache[type] = accessors;
            }
            return accessors;
        }

        private IPropertyHandler? FindHandler([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] System.Type propertyType)
        {
            if (_propertyHandlers.TryGetValue(propertyType, out var handler))
            {
                return handler;
            }

            // Check generic definitions
            if (propertyType.IsGenericType)
            {
                var genericDef = propertyType.GetGenericTypeDefinition();
                if (_propertyHandlers.TryGetValue(genericDef, out handler))
                {
                    return handler;
                }
            }

            // Check interfaces
            foreach (var iface in propertyType.GetInterfaces())
            {
                if (_propertyHandlers.TryGetValue(iface, out handler))
                {
                    return handler;
                }

                if (iface.IsGenericType)
                {
                    var genericIfaceDef = iface.GetGenericTypeDefinition();
                    if (_propertyHandlers.TryGetValue(genericIfaceDef, out handler))
                    {
                        return handler;
                    }
                }
            }

            // Base class walk
            var baseType = propertyType.BaseType;
            while (baseType != null)
            {
                if (_propertyHandlers.TryGetValue(baseType, out handler))
                {
                    return handler;
                }

                baseType = baseType.BaseType;
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
    }
}