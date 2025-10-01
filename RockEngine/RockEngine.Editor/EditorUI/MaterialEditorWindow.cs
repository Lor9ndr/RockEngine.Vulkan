using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorUI.ImGuiRendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RockEngine.Editor.EditorUI
{
    internal class MaterialEditorWindow
    {
        private MaterialAsset _editingMaterial;
        private Vector2 _previewSize = new Vector2(256, 256);
        private readonly AssetManager _assetManager;
        private readonly ImGuiController _imGuiController;

        public MaterialEditorWindow(AssetManager assetManager, ImGuiController imGuiController)
        {
            _assetManager = assetManager;
            _imGuiController = imGuiController;
        }

        public void Draw(MaterialAsset material)
        {
            _editingMaterial = material;

            if (ImGui.Begin("Material Editor##MaterialEditor"))
            {
                DrawMaterialProperties();
                DrawTextureSlots();
                DrawPreview();
            }
            ImGui.End();
        }

        private void DrawTextureSlots()
        {
            if (ImGui.CollapsingHeader("Texture Maps", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawTextureSlot("Albedo", 0);
                DrawTextureSlot("Normal", 1);
                DrawTextureSlot("Metallic", 2);
                DrawTextureSlot("Roughness", 3);
                DrawTextureSlot("AO", 4);
                DrawTextureSlot("Emissive", 5);
            }
        }

        private void DrawTextureSlot(string name, int slotIndex)
        {
            ImGui.PushID($"TextureSlot_{slotIndex}");

            // Get current texture or null
            var textureRef = _editingMaterial.Data.Textures.Count > slotIndex ?
                _editingMaterial.Data.Textures[slotIndex] : null;
            string textureName = textureRef?.Asset?.Name ?? "None";

            // Draw texture slot
            ImGui.Text(name);
            ImGui.SameLine();

            // Drop target
            if (ImGui.Button($"{textureName}##{name}", new Vector2(ImGui.GetContentRegionAvail().X * 0.7f, 0)))
            {
                // Optional: Open texture browser
            }

            if (AssetDragDrop.AcceptAssetDrop(out Guid assetId))
            {
                var textureAsset = _assetManager.GetAsset<TextureAsset>(assetId);
                if (textureAsset != null)
                {
                    // Ensure we have enough slots
                    while (_editingMaterial.Data.Textures.Count <= slotIndex)
                    {
                        _editingMaterial.Data.Textures.Add(null);
                    }

                    _editingMaterial.Data.Textures[slotIndex] = new AssetReference<TextureAsset>(textureAsset);
                    //_editingMaterial.MarkDirty();
                }
            }

            // Clear button
            ImGui.SameLine();
            if (ImGui.Button("X##ClearTexture"))
            {
                if (_editingMaterial.Data.Textures.Count > slotIndex)
                {
                    _editingMaterial.Data.Textures[slotIndex] = null;
                    //_editingMaterial.MarkDirty();
                }
            }

            // Preview on hover
            if (textureRef?.Asset?.Texture is Texture2D texture2D && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                HandleTexturePreview(texture2D);
                ImGui.EndTooltip();
            }

            ImGui.PopID();
        }

        private void DrawMaterialProperties()
        {
            if (ImGui.CollapsingHeader("Material Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Draw material properties here (color, metallic, roughness, etc.)
                // This would use similar drag drop functionality for asset references
            }
        }

        private void DrawPreview()
        {
            if (ImGui.CollapsingHeader("Preview"))
            {
                // Draw material preview
               /* ImGui.Image((IntPtr)_editingMaterial.PreviewTexture?.Handle ?? IntPtr.Zero,
                    _previewSize, new Vector2(0, 1), new Vector2(1, 0));*/
            }
        }

        private void HandleTexturePreview(Texture2D texture)
        {
            if (texture == null) return;

            IntPtr texId = _imGuiController.GetTextureID(texture);
            float previewWidth = Math.Min(ImGui.GetContentRegionAvail().X, 200);
            Vector2 previewSize = new Vector2(previewWidth, previewWidth * (texture.Height / (float)texture.Width));

            ImGui.Image(texId, previewSize);
            ImGui.Text($"Resolution: {texture.Width}x{texture.Height}");
        }
    }
}