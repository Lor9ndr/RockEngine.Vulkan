using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.ResourceProviders;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(MaterialProvider))]
    public class MaterialProviderPropertyHandler : IPropertyHandler
    {
        private readonly Dictionary<string, object> _editingParamOldValues = new();

        public bool CanHandle(Type propertyType) => propertyType == typeof(MaterialProvider);

        public void Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            if (value is not MaterialProvider materialProvider || !materialProvider.IsAssetBased)
                return;

            var material = materialProvider.AssetReference.Asset;
            if (material == null) return;

            ImGui.NewLine();
            ImGui.PushID(material.GetHashCode());

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"Material: {material.Name}");
            ImGui.SameLine();

            if (ImGui.CollapsingHeader($"Textures ({material.Textures.Count})", ImGuiTreeNodeFlags.DefaultOpen))
                DrawTextureList(material, drawer);

            if (ImGui.CollapsingHeader($"Parameters ({material.Parameters.Count})", ImGuiTreeNodeFlags.DefaultOpen))
                DrawParameterList(material, drawer);

            ImGui.PopID();
        }

        private void DrawTextureList(MaterialAsset material, PropertyDrawer drawer)
        {
            for (int i = 0; i < material.Textures.Count; i++)
            {
                var texRef = material.Textures[i];
                ImGui.PushID(i);

                ImGui.BeginGroup();
                DrawTextureThumbnail(texRef, drawer);
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.Text(texRef.Asset?.Name ?? texRef.AssetID.ToString());
                ImGui.TextDisabled($"Slot {i}");
                ImGui.SameLine();
                if (ImGui.SmallButton("X"))
                {
                    var cmd = new ChangeMaterialTextureCommand(material, i, texRef, null);
                    UndoRedoService.Instance.Execute(cmd);
                }
                ImGui.EndGroup();
                ImGui.EndGroup();

                if (ImGui.BeginDragDropTarget())
                {
                    HandleTextureDrop(material, i, drawer);
                    ImGui.EndDragDropTarget();
                }

                ImGui.PopID();
            }
        }

        private void DrawTextureThumbnail(AssetReference<TextureAsset> texRef, PropertyDrawer drawer)
        {
            var textureAsset = texRef.Asset;
            if (textureAsset == null)
            {
                ImGui.Text("[...]");
                _ = drawer.ThumbnailService.GetOrCreateThumbnailAsync(texRef.Asset);
                return;
            }

            var thumbnail = drawer.ThumbnailService.GetOrCreateThumbnailAsync(textureAsset).GetAwaiter().GetResult();
            if (thumbnail?.Texture != null)
            {
                ImGui.Image(drawer.ImGuiController.GetTextureID(thumbnail.Texture), new Vector2(64, 64));
                return;
            }

            if (textureAsset.Texture != null)
            {
                var texId = drawer.ImGuiController.GetTextureID(textureAsset.Texture);
                if (texId != 0)
                {
                    ImGui.Image(texId, new Vector2(64, 64));
                    return;
                }
            }

            ImGui.Text($"[{Icons.QuestionCircle}]");
        }

        private void HandleTextureDrop(MaterialAsset material, int slot, PropertyDrawer drawer)
        {
            if (AssetDragDrop.AcceptAssetDrop(out var assetID))
            {
                var textureAsset = drawer.AssetManager.GetAssetAsync<TextureAsset>(assetID).GetAwaiter().GetResult();
                if (textureAsset != null)
                {
                    var newRef = new AssetReference<TextureAsset>(textureAsset);
                    var oldRef = slot < material.Textures.Count ? material.Textures[slot] : null;
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
                object newValue = null;
                string controlId = $"{material.GetHashCode()}_{kvp.Key}";

                if (ImGui.IsItemActivated())
                    _editingParamOldValues[controlId] = value;

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
                    material.UpdateParameter(kvp.Key, newValue);

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