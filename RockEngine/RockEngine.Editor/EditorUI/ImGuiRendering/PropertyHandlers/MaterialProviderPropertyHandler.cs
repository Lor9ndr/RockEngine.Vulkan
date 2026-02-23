using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
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
            var provider = value as MaterialProvider;

            ImGui.NewLine();
            // Main control: button showing current material name
            string label = $"{accessor.DisplayName}: {GetMaterialDisplayName(provider)}";
            ImGui.AlignTextToFramePadding();
            ImGui.BeginGroup();
            ImGui.Text(label);

            ImGui.SameLine();
            if (ImGui.SmallButton($"Clear##{accessor.Name}"))
            {
                accessor.SetValue(component, null);
            }

            // Drag-drop target
            if (ImGui.BeginDragDropTarget())
            {
                HandleDragDrop(component, accessor, drawer);
                ImGui.EndDragDropTarget();
            }

            // Tooltip on hover
            ImGui.EndGroup();
            if (ImGui.IsItemHovered() && provider != null)
            {
                ImGui.BeginTooltip();
                DrawMaterialTooltipContent(provider, drawer);
                ImGui.EndTooltip();
            }
        }

        private string GetMaterialDisplayName(MaterialProvider provider)
        {
            if (provider == null) return "None";
            if (provider.IsAssetBased && provider.AssetReference?.Asset is MaterialAsset mat)
                return mat.Name;
            if (provider.DirectMaterial != null)
                return $"Direct: {provider.DirectMaterial.Name}";
            return "Material Provider";
        }

        private void HandleDragDrop(IComponent component, UIPropertyAccessor accessor, PropertyDrawer drawer)
        {
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var materialAsset = drawer.AssetManager.GetAssetAsync<MaterialAsset>(assetID).GetAwaiter().GetResult();
                if (materialAsset != null)
                {
                    var provider = new MaterialProvider(new AssetReference<MaterialAsset>(materialAsset));
                    accessor.SetValue(component, provider);
                }
            }
        }

        private void DrawMaterialTooltipContent(MaterialProvider provider, PropertyDrawer drawer)
        {
            ImGui.Text($"Type: Material");

            if (provider.IsAssetBased && provider.AssetReference?.Asset is MaterialAsset materialAsset)
            {
                ImGui.Text($"Asset: {materialAsset.Name}");
                ImGui.Text($"Path: {materialAsset.Path}");

                if (materialAsset.Textures.Count > 0)
                {
                    ImGui.Separator();
                    ImGui.Text("Textures:");

                    // Use a table for clean layout: thumbnail | name
                    if (ImGui.BeginTable("##materialTextures", 2, ImGuiTableFlags.SizingFixedFit))
                    {
                        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 128);
                        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch);

                        foreach (var texRef in materialAsset.Textures)
                        {
                            ImGui.TableNextRow();

                            // Thumbnail column
                            ImGui.TableSetColumnIndex(0);
                            DrawTextureThumbnail(texRef, drawer);

                            // Name column
                            ImGui.TableSetColumnIndex(1);
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(texRef.Asset?.Name ?? texRef.AssetID.ToString());
                        }
                        ImGui.EndTable();
                    }
                }
            }
            else
            {
                ImGui.Text("Source: Direct Object");
                if (provider.DirectMaterial != null)
                {
                    ImGui.Text($"Material: {provider.DirectMaterial.Name}");
                }
            }
        }

        private void DrawTextureThumbnail(AssetReference<TextureAsset> texRef, PropertyDrawer drawer)
        {
            var textureAsset = texRef.Asset;
            if (textureAsset == null)
            {
                // Not loaded yet – show placeholder and start loading
                ImGui.Text("[...]");
                _ = drawer.ThumbnailService.GetOrCreateThumbnailAsync(texRef.Asset);
                return;
            }

            // Try to get cached thumbnail
            var thumbnail = drawer.ThumbnailService.GetOrCreateThumbnailAsync(textureAsset).GetAwaiter().GetResult();
            if (thumbnail != null )
            {
                
                ImGui.Image(drawer.ImGuiController.GetTextureID(thumbnail.Texture), new Vector2(128, 128));
                return;
            }

            // Fallback: use original texture if available
            if (textureAsset.Texture != null)
            {
                var texId = drawer.ImGuiController.GetTextureID(textureAsset.Texture);
                if (texId != 0)
                {
                    ImGui.Image(texId, new Vector2(128, 128));
                    return;
                }
            }

            // No thumbnail and no GPU texture
            ImGui.Text($"[{Icons.QuestionCircle}]");
        }
    }
}