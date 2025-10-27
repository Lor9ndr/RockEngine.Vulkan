using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.ResourceProviders;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(MaterialProvider))]
    public class MaterialProviderPropertyHandler : IPropertyHandler
    {
        public bool CanHandle(Type propertyType) => propertyType == typeof(MaterialProvider);

        public async ValueTask Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            var materialProvider = value as MaterialProvider;

            string currentName = GetCurrentResourceName(materialProvider);
            float buttonWidth = 458;

            ImGui.Button($"{accessor.DisplayName}: {currentName}");

            await HandleAssetDragDrop(component, accessor, drawer);
            HandleMaterialSpecificUI(component, accessor, materialProvider, drawer);
            HandleMaterialTooltip(materialProvider, drawer);
        }

        private string GetCurrentResourceName(MaterialProvider materialProvider)
        {
            if (materialProvider == null)
            {
                return "None";
            }

            if (materialProvider.IsAssetBased && materialProvider.AssetReference?.Asset != null)
            {
                return materialProvider.AssetReference.Asset.Name;
            }
            else if (materialProvider.DirectMaterial != null)
            {
                return $"Material ({materialProvider.DirectMaterial.Name})";
            }

            return "Material Provider";
        }

        private async ValueTask HandleAssetDragDrop(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var materialAsset = await drawer.AssetManager.GetAssetAsync<MaterialAsset>(assetID);
                if (materialAsset != null)
                {
                    var materialProvider = new MaterialProvider(new AssetReference<MaterialAsset>(materialAsset));
                    accessor.SetValue(component, materialProvider);
                }
            }
        }

        private void HandleMaterialSpecificUI(IComponent component, UIPropertyAccessor accessor, MaterialProvider materialProvider, PropertyDrawer drawer)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear##Clear" + accessor.Name))
            {
                accessor.SetValue(component, null);
            }

            ImGui.SameLine();
            if (ImGui.Button("New##New" + accessor.Name))
            {
                CreateNewMaterialForProperty(component, accessor, drawer);
            }

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Create Default Material"))
                {
                    CreateDefaultMaterial(component, accessor, drawer);
                }

                if (materialProvider?.DirectMaterial != null && ImGui.MenuItem("Convert to Asset"))
                {
                    ConvertToAsset(component, accessor, materialProvider, drawer);
                }

                ImGui.EndPopup();
            }
        }

        private void HandleMaterialTooltip(MaterialProvider materialProvider, PropertyDrawer drawer)
        {
            if (materialProvider != null && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                DrawMaterialInfo(materialProvider);
                ImGui.EndTooltip();
            }
        }

        private void DrawMaterialInfo(MaterialProvider materialProvider)
        {
            ImGui.Text("Type: Material");

            if (materialProvider.IsAssetBased)
            {
                ImGui.Text("Source: Asset");
                if (materialProvider.AssetReference?.Asset != null)
                {
                    ImGui.Text($"Asset: {materialProvider.AssetReference.Asset.Name}");
                    ImGui.Text($"Path: {materialProvider.AssetReference.Asset.Path}");
                }
            }
            else
            {
                ImGui.Text("Source: Direct Object");
                if (materialProvider.DirectMaterial != null)
                {
                    ImGui.Text($"Material: {materialProvider.DirectMaterial.Name}");
                    // Add more material info as needed
                }
            }
        }

        private void CreateNewMaterialForProperty(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            // This could open a material creation dialog
            // For now, we'll just set it to null
            accessor.SetValue(component, null);
        }

        private void CreateDefaultMaterial(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            var defaultMaterial = new Material("Default Material");
            var materialProvider = new MaterialProvider(defaultMaterial);
            accessor.SetValue(component, materialProvider);
        }

        private void ConvertToAsset(IComponent component, UIPropertyAccessor accessor, MaterialProvider materialProvider, PropertyDrawer drawer)
        {
            // Convert direct material to asset
        }
    }
}