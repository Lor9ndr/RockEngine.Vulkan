using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.ResourceProviders;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(MeshProvider))]
    public class MeshProviderPropertyHandler : IPropertyHandler
    {
        public bool CanHandle(Type propertyType) => propertyType == typeof(MeshProvider);

        public void Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            var meshProvider = value as MeshProvider;

            string currentName = GetCurrentResourceName(meshProvider);
            float buttonWidth = ImGui.GetContentRegionAvail().X - 120;

            ImGui.Button($"{accessor.DisplayName}: {currentName}");

            HandleAssetDragDrop(component, accessor, drawer);
            HandleMeshSpecificUI(component, accessor, meshProvider, drawer);
            HandleMeshTooltip(meshProvider, drawer);
        }

        private string GetCurrentResourceName(MeshProvider meshProvider)
        {
            if (meshProvider == null)
                return "None";

            if (meshProvider.IsAssetBased && meshProvider.AssetReference?.Asset != null)
                return meshProvider.AssetReference.Asset.Name;

            if (meshProvider.DirectMesh != null)
                return $"Mesh ({meshProvider.DirectMesh.VerticesCount} vertices)";

            return "Mesh Provider";
        }

        private void HandleAssetDragDrop(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var meshAsset = drawer.AssetManager.GetAssetAsync<MeshAsset>(assetID).GetAwaiter().GetResult();
                if (meshAsset != null)
                {
                    var oldProvider = accessor.GetValue(component) as MeshProvider;
                    var newProvider = new MeshProvider(new AssetReference<MeshAsset>(meshAsset));
                    var cmd = new ChangePropertyCommand<MeshProvider>(component, accessor, oldProvider, newProvider);
                    UndoRedoService.Instance.Execute(cmd);
                }
            }
        }

        private void HandleMeshSpecificUI(IComponent component, UIPropertyAccessor accessor, MeshProvider meshProvider, PropertyDrawer drawer)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear##Clear" + accessor.Name))
            {
                var oldProvider = accessor.GetValue(component) as MeshProvider;
                var cmd = new ChangePropertyCommand<MeshProvider>(component, accessor, oldProvider, null);
                UndoRedoService.Instance.Execute(cmd);
            }

            ImGui.SameLine();

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Create Primitive"))
                    ShowPrimitiveCreationMenu(component, accessor, drawer);

                if (meshProvider?.DirectMesh != null && ImGui.MenuItem("Convert to Asset"))
                    ConvertToAsset(component, accessor, meshProvider, drawer);

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
                }
            }
        }

        private void ShowPrimitiveCreationMenu(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            // Placeholder – implement primitive creation and push a command
            // Example:
            // var oldProvider = accessor.GetValue(component) as MeshProvider;
            // var newProvider = CreatePrimitiveMeshProvider("Cube");
            // var cmd = new ChangePropertyCommand<MeshProvider>(component, accessor, oldProvider, newProvider);
            // UndoRedoService.Instance.Execute(cmd);
        }

        private void ConvertToAsset(IComponent component, UIPropertyAccessor accessor, MeshProvider meshProvider, PropertyDrawer drawer)
        {
            // Placeholder – convert direct mesh to asset and push command
        }
    }
}