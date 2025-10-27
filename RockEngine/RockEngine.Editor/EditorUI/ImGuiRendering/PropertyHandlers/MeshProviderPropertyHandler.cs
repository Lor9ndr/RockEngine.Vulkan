using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.ResourceProviders;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(MeshProvider))]
    public class MeshProviderPropertyHandler : IPropertyHandler
    {
        public bool CanHandle(Type propertyType) => propertyType == typeof(MeshProvider);

        public async ValueTask Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            var meshProvider = value as MeshProvider;

            string currentName = GetCurrentResourceName(meshProvider);
            float buttonWidth = ImGui.GetContentRegionAvail().X - 120;

            ImGui.Button($"{accessor.DisplayName}: {currentName}");

            await HandleAssetDragDrop(component, accessor, drawer);
            HandleMeshSpecificUI(component, accessor, meshProvider, drawer);
            HandleMeshTooltip(meshProvider, drawer);
        }

        private string GetCurrentResourceName(MeshProvider meshProvider)
        {
            if (meshProvider == null)
            {
                return "None";
            }

            if (meshProvider.IsAssetBased && meshProvider.AssetReference?.Asset != null)
            {
                return meshProvider.AssetReference.Asset.Name;
            }
            else if (meshProvider.DirectMesh != null)
            {
                return $"Mesh ({meshProvider.DirectMesh.VerticesCount} vertices)";
            }

            return "Mesh Provider";
        }

        private async ValueTask HandleAssetDragDrop(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var meshAsset = await drawer.AssetManager.GetAssetAsync<MeshAsset>(assetID);
                if (meshAsset != null)
                {
                    var meshProvider = new MeshProvider(new AssetReference<MeshAsset>(meshAsset));
                    accessor.SetValue(component, meshProvider);
                }
            }
        }

        private void HandleMeshSpecificUI(IComponent component, UIPropertyAccessor accessor, MeshProvider meshProvider, PropertyDrawer drawer)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear##Clear" + accessor.Name))
            {
                accessor.SetValue(component, null);
            }

            ImGui.SameLine();

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Create Primitive"))
                {
                    ShowPrimitiveCreationMenu(component, accessor, drawer);
                }

                if (meshProvider?.DirectMesh != null && ImGui.MenuItem("Convert to Asset"))
                {
                    ConvertToAsset(component, accessor, meshProvider, drawer);
                }

                ImGui.EndPopup();
            }
        }

        private void HandleMeshTooltip(MeshProvider meshProvider, PropertyDrawer drawer)
        {
            if (meshProvider != null && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                DrawMeshInfo(meshProvider);
                ImGui.EndTooltip();
            }
        }

        private void DrawMeshInfo(MeshProvider meshProvider)
        {
            ImGui.Text("Type: Mesh");

            if (meshProvider.IsAssetBased)
            {
                ImGui.Text("Source: Asset");
                if (meshProvider.AssetReference?.Asset != null)
                {
                    ImGui.Text($"Asset: {meshProvider.AssetReference.Asset.Name}");
                    ImGui.Text($"Path: {meshProvider.AssetReference.Asset.Path}");
                }
            }
            else
            {
                ImGui.Text("Source: Direct Object");
                if (meshProvider.DirectMesh != null)
                {
                    ImGui.Text($"Vertices: {meshProvider.DirectMesh.VerticesCount}");
                    ImGui.Text($"Indices: {meshProvider.DirectMesh.IndicesCount}");
                    ImGui.Text($"Has Indices: {meshProvider.DirectMesh.HasIndices}");
                }
            }
        }


        private void ShowPrimitiveCreationMenu(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            // Implementation for creating primitive meshes (cube, sphere, etc.)
            ImGui.OpenPopup("PrimitiveCreationPopup");

            if (ImGui.BeginPopup("PrimitiveCreationPopup"))
            {
                if (ImGui.MenuItem("Cube"))
                {
                    // Create cube mesh
                }
                if (ImGui.MenuItem("Sphere"))
                {
                    // Create sphere mesh
                }
                if (ImGui.MenuItem("Plane"))
                {
                    // Create plane mesh
                }
                ImGui.EndPopup();
            }
        }

        private void ConvertToAsset(IComponent component, UIPropertyAccessor accessor, MeshProvider meshProvider, PropertyDrawer drawer)
        {
            // Convert direct mesh to asset
            // This would involve saving the mesh data to a file and creating a MeshAsset
        }
    }
}