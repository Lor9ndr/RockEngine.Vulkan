using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.UIAttributes;

using System.Numerics;
using System.Reflection;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public class PropertyDrawer
    {
        private readonly AssetManager _assetManager;
        private readonly ImGuiController _imGuiController;

        public PropertyDrawer(AssetManager assetManager, ImGuiController imGuiController)
        {
            _assetManager = assetManager;
            _imGuiController = imGuiController;
        }

        public void DrawProperty(IComponent component, PropertyInfo property)
        {
            if (!property.CanRead) return;

            var uiAttr = property.GetCustomAttribute<UIEditableAttribute>();
            string label = uiAttr?.DisplayName ?? property.Name;

            ImGui.PushID($"{component.GetType().Name}_{property.Name}");

            if (!property.CanWrite)
            {
                ImGui.BeginDisabled();
            }

            // Handle different property types
            if (property.PropertyType == typeof(AssetReference<TextureAsset>))
            {
                HandleTextureProperty(component, property, label);
            }
            else if (property.PropertyType == typeof(AssetReference<MaterialAsset>))
            {
                HandleMaterialProperty(component, property, label);
            }
            else if (property.PropertyType == typeof(AssetReference<MeshAsset>))
            {
                HandleMeshProperty(component, property, label);
            }
            else if (property.PropertyType == typeof(float))
            {
                HandleFloatProperty(component, property, label);
            }
            else if (property.PropertyType == typeof(Vector3))
            {
                HandleVector3Property(component, property, label);
            }
            else if (property.PropertyType.IsEnum)
            {
                HandleEnumProperty(component, property, label);
            }
            else if (property.PropertyType == typeof(bool))
            {
                HandleBoolProperty(component, property, label);
            }
            else
            {
                var value = property.GetValue(component);
                ImGui.Text($"{label}: {value}");
            }

            if (!property.CanWrite)
            {
                ImGui.EndDisabled();
            }

            ImGui.PopID();
        }

        private void HandleTextureProperty(IComponent component, PropertyInfo property, string label)
        {
            var textureRef = (AssetReference<TextureAsset>)property.GetValue(component);
            string currentName = textureRef?.Asset?.Name ?? "None";

            ImGui.Button($"{label}: {currentName}", new Vector2(ImGui.GetContentRegionAvail().X, 0));

            // Drag and drop implementation would go here
             if (AssetDragDrop.AcceptAssetDrop(out var assetID))
             {
                 var textureAsset = _assetManager.GetAsset<TextureAsset>(assetID);
                 if (textureAsset != null)
                 {
                     property.SetValue(component, new AssetReference<TextureAsset>(textureAsset));
                 }
             }

             if (textureRef?.Asset != null && AssetDragDrop.BeginDragDropSource(textureRef.Asset.ID, textureRef.Asset.Name))
             {
                 // Drag source handling
             }

            if (textureRef?.Asset?.Texture is Texture2D texture2D && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                HandleTexturePreview(texture2D);
                ImGui.EndTooltip();
            }
        }

        private void HandleMaterialProperty(IComponent component, PropertyInfo property, string label)
        {
            var materialRef = (AssetReference<MaterialAsset>)property.GetValue(component);
            string currentName = materialRef?.Asset?.Name ?? "None";

            float buttonWidth = ImGui.GetContentRegionAvail().X - 120;
            ImGui.Button($"{label}: {currentName}", new Vector2(buttonWidth, 0));

            // Drag and drop would go here
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var materialAsset = _assetManager.GetAsset<MaterialAsset>(assetID);
                if (materialAsset != null)
                {
                    property.SetValue(component, new AssetReference<MaterialAsset>(materialAsset));
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear##ClearMaterial"))
            {
                property.SetValue(component, new AssetReference<MaterialAsset>());
            }

            ImGui.SameLine();
            if (ImGui.Button("New##NewMaterial"))
            {
                CreateNewMaterialForProperty(component, property);
            }
        }

        private void HandleMeshProperty(IComponent component, PropertyInfo property, string label)
        {
            var meshRef = (AssetReference<MeshAsset>)property.GetValue(component);
            string currentName = meshRef?.Asset?.Name ?? "None";

            ImGui.Button($"{label}: {currentName}", new Vector2(ImGui.GetContentRegionAvail().X, 0));

             if (AssetDragDrop.AcceptAssetDrop(out Guid assetId))
             {
                 var meshAsset = _assetManager.GetAsset<MeshAsset>(assetId);
                 if (meshAsset != null)
                 {
                     property.SetValue(component, new AssetReference<MeshAsset>(meshAsset));
                 }
             }
        }

        private void HandleFloatProperty(IComponent component, PropertyInfo property, string label)
        {
            float value = (float)property.GetValue(component);
            var range = property.GetCustomAttribute<RangeAttribute>();

            if (range != null)
                ImGui.DragFloat(label, ref value, 0.1f, range.Min, range.Max);
            else
                ImGui.DragFloat(label, ref value);

            if (property.CanWrite)
            {
                property.SetValue(component, value);
            }
        }

        private void HandleVector3Property(IComponent component, PropertyInfo property, string label)
        {
            Vector3 value = (Vector3)property.GetValue(component);
            bool isColor = property.GetCustomAttribute<ColorAttribute>() != null;

            if (isColor)
                ImGui.ColorEdit3(label, ref value);
            else
                ImGui.DragFloat3(label, ref value);

            if (property.CanWrite)
            {
                property.SetValue(component, value);
            }
        }

        private void HandleEnumProperty(IComponent component, PropertyInfo property, string label)
        {
            Enum value = (Enum)property.GetValue(component);
            if (ImGui.BeginCombo(label, value.ToString()))
            {
                foreach (Enum enumValue in Enum.GetValues(property.PropertyType))
                {
                    bool isSelected = value.Equals(enumValue);
                    if (ImGui.Selectable(enumValue.ToString(), isSelected) && property.CanWrite)
                    {
                        property.SetValue(component, enumValue);
                    }
                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
        }

        private void HandleBoolProperty(IComponent component, PropertyInfo property, string label)
        {
            bool value = (bool)property.GetValue(component);
            if (ImGui.Checkbox(label, ref value) && property.CanWrite)
            {
                property.SetValue(component, value);
            }
        }

        private void HandleTexturePreview(Texture2D texture)
        {
            if (texture == null) return;

            nint texId = _imGuiController.GetTextureID(texture);
            float previewWidth = Math.Min(ImGui.GetContentRegionAvail().X, 200);
            Vector2 previewSize = new Vector2(previewWidth, previewWidth * (texture.Height / (float)texture.Width));

            ImGui.Image(texId, previewSize);
            ImGui.Text($"Resolution: {texture.Width}x{texture.Height}");
            ImGui.Text($"Mip Levels: {texture.LoadedMipLevels}/{texture.TotalMipLevels}");
        }

        private void CreateNewMaterialForProperty(IComponent component, PropertyInfo property)
        {
            try
            {
                
            }
            catch (Exception ex)
            {
                // Log error
            }
        }
    }
}