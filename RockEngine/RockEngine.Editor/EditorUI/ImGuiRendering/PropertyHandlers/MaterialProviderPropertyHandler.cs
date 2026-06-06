using System.Numerics;
using ImGuiNET;
using NLog;
using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.ResourceProviders;
using RockEngine.Editor.EditorUI.Thumbnails;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(MaterialProvider))]
    public class MaterialProviderPropertyHandler : IPropertyHandler
    {
        private readonly Dictionary<string, object> _editingParamOldValues = new();
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public bool CanHandle(Type propertyType) => propertyType == typeof(MaterialProvider);

        public void Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            if (value is not MaterialProvider materialProvider || !materialProvider.IsAssetBased)
            {
                return;
            }

            var material = materialProvider.AssetReference.Asset;
            if (material == null)
            {
                return;
            }

            ImGui.NewLine();
            ImGui.PushID(material.GetHashCode());

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"Material: {material.Name}");
            ImGui.SameLine();

            if (ImGui.CollapsingHeader($"Textures ({material.Textures.Count})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawTextureList(material, drawer);
            }

            if (ImGui.CollapsingHeader($"Parameters ({material.Parameters.Count})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawParameterList(material, drawer);
            }

            ImGui.PopID();
        }

        public void  DrawTextureList(MaterialAsset material, PropertyDrawer drawer)
        {
            if (material.MaterialInstance is null)
            {
                return;
            }

            foreach (var pass in material.MaterialInstance.Passes)
            {
                ImGui.PushID(pass.Key);
                ImGui.BeginGroup();

                foreach (var metadata in pass.Value.Pipeline.Layout.ShadersMetadata)
                {
                    if (metadata is null)
                    {
                        continue;
                    }

                    foreach (var item in metadata.Textures)
                    {
                        // Check if the slot already has a texture
                        material.Textures.TryGetValue(item.Name, out var texRef);

                        // Begin a table with 3 columns for this slot
                        if (ImGui.BeginTable($"##texSlot_{item.Name}", 3,
                            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingFixedFit))
                        {
                            // Column 0: Slot name
                            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 100);
                            // Column 1: Thumbnail (64x64)
                            ImGui.TableSetupColumn("Thumbnail", ImGuiTableColumnFlags.WidthFixed, 72);
                            // Column 2: Remove button
                            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 20);

                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            ImGui.TextDisabled(item.Name);

                            ImGui.TableNextColumn();
                            // Draw thumbnail or placeholder, and make it a drop target
                            bool hasTexture = DrawTextureThumbnail(texRef, drawer, out bool drawn);
                            if (!hasTexture)
                            {
                                // If no texture, draw a grey placeholder rectangle that also acts as drop target
                                ImGui.InvisibleButton($"##drop_{item.Name}", new System.Numerics.Vector2(64, 64));
                                HandleTextureDrop(material, item.Name, drawer);

                                // Draw the placeholder frame
                                var min = ImGui.GetItemRectMin();
                                var max = ImGui.GetItemRectMax();
                                ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg));
                                ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border));
                                ImGui.SetCursorScreenPos(min);
                                ImGui.TextDisabled("None");
                            }
                            else
                            {
                                // The thumbnail image itself can accept drops
                                HandleTextureDrop(material, item.Name, drawer);
                            }

                            ImGui.TableNextColumn();
                            if (texRef != null && hasTexture) // only show remove button for existing textures
                            {
                                if (ImGui.SmallButton($"X##{item.Name}"))
                                {
                                    var cmd = new ChangeMaterialTextureCommand(material, item.Name, texRef, null);
                                    UndoRedoService.Instance.Execute(cmd);
                                }
                            }

                            ImGui.EndTable();
                        }
                    }
                }
                ImGui.EndGroup();
                ImGui.PopID();
            }
        }

        private bool DrawTextureThumbnail(AssetReference<TextureAsset> texRef, PropertyDrawer drawer, out bool drawn)
        {
            drawn = false;
            if (texRef == null)
            {
                return false;
            }

            var textureAsset = texRef.Asset;
            if (textureAsset == null)
            {
                ImGui.Text("[...]");
                // Trigger async loading – later you'd update the UI when the thumbnail is ready
                _ = drawer.ThumbnailService.GetOrCreateThumbnailAsync(texRef.Asset);
                return false;
            }

            // Try to get a thumbnail (non-blocking would be ideal; here we just check if it’s ready)
            Thumbnail? thumbnail = null;
            try
            {
                // If this must be synchronous, at least handle the case where thumbnail isn't ready
                thumbnail = drawer.ThumbnailService.GetOrCreateThumbnailAsync(textureAsset).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            if (thumbnail?.Texture != null)
            {
                ImGui.Image(drawer.ImGuiController.GetTextureID(thumbnail.Texture), new Vector2(64, 64));
                drawn = true;
                return true;
            }

            if (textureAsset.Texture != null)
            {
                var texId = drawer.ImGuiController.GetTextureID(textureAsset.Texture);
                if (texId != 0)
                {
                    ImGui.Image(texId, new Vector2(64, 64));
                    drawn = true;
                    return true;
                }
            }

            ImGui.Text($"[{Icons.QuestionCircle}]");
            drawn = true;
            return true;
        }

        private void HandleTextureDrop(MaterialAsset material, string slot, PropertyDrawer drawer)
        {
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var textureAsset = drawer.AssetManager.GetAssetAsync<TextureAsset>(assetID).GetAwaiter().GetResult();
                if (textureAsset != null)
                {
                    var newRef = new AssetReference<TextureAsset>(textureAsset);

                    material.Textures.TryGetValue(slot, out AssetReference<TextureAsset>? oldRef);

                    var cmd = new ChangeMaterialTextureCommand(material, slot, oldRef, newRef);
                    UndoRedoService.Instance.Execute(cmd);
                }
            }
        }

        private void DrawParameterList(MaterialAsset material, PropertyDrawer drawer)
        {
            foreach (var kvp in material.Parameters.ToList())
            {
                ImGui.PushID(kvp.Key);

                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{kvp.Key}:");
                ImGui.SameLine();

                var value = kvp.Value;
                bool changed = false;
                object? newValue = null;
                string controlId = $"{material.GetHashCode()}_{kvp.Key}";

                if (ImGui.IsItemActivated())
                {
                    _editingParamOldValues[controlId] = value;
                }

                switch (value)
                {
                    case float f:
                        float fVal = f;
                        changed = ImGui.DragFloat("##value", ref fVal, 0.01f);
                        newValue = fVal;
                        break;
                    case Vector3 v3:
                        Vector3 v3Val = v3;
                        changed = ImGui.DragFloat3("##value", ref v3Val, 0.01f);
                        newValue = v3Val;
                        break;
                    case Vector4 v4:
                        Vector4 v4Val = v4;
                        changed = ImGui.DragFloat4("##value", ref v4Val, 0.01f);
                        newValue = v4Val;
                        break;
                    case int i:
                        int iVal = i;
                        changed = ImGui.DragInt("##value", ref iVal);
                        newValue = iVal;
                        break;
                    case bool b:
                        bool bVal = b;
                        changed = ImGui.Checkbox("##value", ref bVal);
                        newValue = bVal;
                        break;
                    default:
                        ImGui.Text(value?.ToString() ?? "null");
                        break;
                }

                if (changed)
                {
                    material.UpdateParameter(kvp.Key, newValue);
                }

                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    if (_editingParamOldValues.TryGetValue(controlId, out var oldValue))
                    {
                        var cmd = new ChangeMaterialParameterCommand(material, kvp.Key, oldValue, newValue);
                        UndoRedoService.Instance.Execute(cmd);
                        _editingParamOldValues.Remove(controlId);
                    }
                }

                ImGui.PopID();
            }
        }
    }
}