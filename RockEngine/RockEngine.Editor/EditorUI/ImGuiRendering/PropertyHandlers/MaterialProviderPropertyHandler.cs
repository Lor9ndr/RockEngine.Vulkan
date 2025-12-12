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

        public void Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            var materialProvider = value as MaterialProvider;

            string currentName = GetCurrentResourceName(materialProvider);
            float buttonWidth = 458;
            ImGui.NewLine();
            ImGui.Button($"{accessor.DisplayName}: {currentName}");

            HandleAssetDragDrop(component, accessor, drawer);
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

        private void HandleAssetDragDrop(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var materialAsset = drawer.AssetManager.GetAssetAsync<MaterialAsset>(assetID).GetAwaiter().GetResult();
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

            if (ImGui.BeginPopupContextItem())
            {
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
    }
}