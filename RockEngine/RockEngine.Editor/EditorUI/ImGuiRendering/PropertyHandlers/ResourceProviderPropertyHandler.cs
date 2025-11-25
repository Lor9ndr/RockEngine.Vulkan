using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.ResourceProviders;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(IResourceProvider<>))]
    public class ResourceProviderPropertyHandler : IPropertyHandler
    {
        public bool CanHandle(Type propertyType) =>
            propertyType.IsGenericType &&
            propertyType.GetGenericTypeDefinition() == typeof(IResourceProvider<>);

        public async ValueTask Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            var resourceProvider = value as IResourceProvider;
            var resourceType = accessor.PropertyType.GetGenericArguments()[0];

            string currentName = GetCurrentResourceName(resourceProvider, resourceType);
            string typeName = resourceType.Name;

            float buttonWidth = ImGui.GetContentRegionAvail().X - 120;
            ImGui.Button($"{accessor.DisplayName}: {currentName}", new Vector2(buttonWidth, 0));

            await HandleAssetDragDrop(component, accessor, resourceType, drawer);
            HandleResourceSpecificUI(component, accessor, resourceProvider, resourceType, drawer);
            HandleResourceTooltip(resourceProvider, resourceType, drawer);

        }

        private string GetCurrentResourceName(IResourceProvider resourceProvider, Type resourceType)
        {
            if (resourceProvider == null)
            {
                return "None";
            }

            // For asset-based providers
            if (resourceProvider is MeshProvider meshProvider)
            {
                if (meshProvider.IsAssetBased && meshProvider.AssetReference?.Asset != null)
                {
                    return meshProvider.AssetReference.Asset.Name;
                }
                else if (meshProvider.DirectMesh != null)
                {
                    return $"Mesh ({meshProvider.DirectMesh.VerticesCount} vertices)";
                }
            }

            // For material providers (you'll need to add similar properties to MaterialProvider)
            if (resourceProvider is MaterialProvider materialProvider)
            {
                // You'll need to add IsAssetBased and AssetReference properties to MaterialProvider
                // For now, using reflection or checking internal state
                return "Material"; // Placeholder
            }

            // Generic fallback
            return $"{resourceType.Name} Provider";
        }

        private async Task HandleAssetDragDrop(IComponent component, UIPropertyAccessor accessor, Type resourceType, PropertyDrawer drawer)
        {
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                // Determine the asset type based on the resource type
                Type assetType = GetAssetTypeForResourceType(resourceType);
                if (assetType != null)
                {
                    var asset = await drawer.AssetManager.GetAssetAsync<IAsset>(assetID);
                    if (asset != null)
                    {
                        var provider = CreateProviderForAsset(accessor.PropertyType, asset);
                        accessor.SetValue(component, provider);
                    }
                }
            }
        }

        private Type GetAssetTypeForResourceType(Type resourceType)
        {
            // Map resource types to asset types
            if (resourceType == typeof(IMesh) || resourceType.Name.Contains("Mesh"))
            {
                return typeof(MeshAsset);
            }

            if (resourceType == typeof(Material) || resourceType.Name.Contains("Material"))
            {
                return typeof(MaterialAsset);
            }

            // Add more mappings as needed
            return null;
        }

        private object CreateProviderForAsset(Type providerType, IAsset asset)
        {
            var resourceType = providerType.GetGenericArguments()[0];

            if (resourceType == typeof(IMesh) && asset is MeshAsset meshAsset)
            {
                var assetRefType = typeof(AssetReference<>).MakeGenericType(typeof(MeshAsset));
                var assetRef = Activator.CreateInstance(assetRefType, meshAsset);
                return new MeshProvider((AssetReference<MeshAsset>)assetRef);
            }

            if (resourceType == typeof(Material) && asset is MaterialAsset materialAsset)
            {
                var assetRefType = typeof(AssetReference<>).MakeGenericType(typeof(MaterialAsset));
                var assetRef = Activator.CreateInstance(assetRefType, materialAsset);
                return new MaterialProvider((AssetReference<MaterialAsset>)assetRef);
            }

            // Use reflection for other types
            var providerConstructor = providerType.GetConstructor(new[] {
                typeof(AssetReference<>).MakeGenericType(asset.GetType())
            });

            if (providerConstructor != null)
            {
                var assetRefType = typeof(AssetReference<>).MakeGenericType(asset.GetType());
                var assetRef = Activator.CreateInstance(assetRefType, asset);
                return providerConstructor.Invoke(new[] { assetRef });
            }

            return null;
        }

        private void HandleResourceSpecificUI(IComponent component, UIPropertyAccessor accessor, IResourceProvider resourceProvider, Type resourceType, PropertyDrawer drawer)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear##Clear" + accessor.Name))
            {
                accessor.SetValue(component, null);
            }

            ImGui.SameLine();
            if (ImGui.Button("New##New" + accessor.Name))
            {
                CreateNewResourceForProperty(component, accessor, resourceType, drawer);
            }

            // Additional context menu for more options
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Create from current selection"))
                {
                    CreateFromSelection(component, accessor, resourceType, drawer);
                }

                if (ImGui.MenuItem("Convert to asset"))
                {
                    ConvertToAsset(component, accessor, resourceProvider, drawer);
                }

                ImGui.EndPopup();
            }
        }

        private void HandleResourceTooltip(IResourceProvider resourceProvider, Type resourceType, PropertyDrawer drawer)
        {
            if (resourceProvider != null && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                DrawResourceInfo(resourceProvider, resourceType);
                ImGui.EndTooltip();
            }
        }

        private void DrawResourceInfo(IResourceProvider resourceProvider, Type resourceType)
        {
            ImGui.Text($"Type: {resourceType.Name}");

            if (resourceProvider is MeshProvider meshProvider)
            {
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

            // Add similar info for MaterialProvider and other providers
        }

        private void CreateNewResourceForProperty(IComponent component, UIPropertyAccessor accessor, Type resourceType, PropertyDrawer drawer)
        {
            try
            {
                // Create a new default resource based on type
                if (resourceType == typeof(IMesh))
                {
                    // Create a default mesh (e.g., cube, sphere, etc.)
                    // This would open a dialog or use a default mesh
                    // For now, we'll set it to null
                    accessor.SetValue(component, null);
                }
                else if (resourceType == typeof(Material))
                {
                    // Create a default material
                    var defaultMaterial = CreateDefaultMaterial();
                    var materialProvider = new MaterialProvider(defaultMaterial);
                    accessor.SetValue(component, materialProvider);
                }
            }
            catch (Exception ex)
            {
                // Log error
                Console.WriteLine($"Error creating new resource: {ex.Message}");
            }
        }

        private Material CreateDefaultMaterial()
        {
            // Create and return a default material
            // This is a simplified example
            return new Material("Default Material");
        }

        private void CreateFromSelection(IComponent component, UIPropertyAccessor accessor, Type resourceType, PropertyDrawer drawer)
        {
            // Implementation would depend on your selection system
            // This could create a resource from currently selected objects in the scene
        }

        private void ConvertToAsset(IComponent component, UIPropertyAccessor accessor, IResourceProvider resourceProvider, PropertyDrawer drawer)
        {
            // Convert a direct object to an asset
            if (resourceProvider is MeshProvider meshProvider && !meshProvider.IsAssetBased)
            {
                // Implementation to save the mesh as an asset
                // This would open a save dialog and create a new MeshAsset
            }
        }
    }
}