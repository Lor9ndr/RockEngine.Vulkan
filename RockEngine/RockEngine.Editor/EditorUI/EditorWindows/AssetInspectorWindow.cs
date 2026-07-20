using ImGuiNET;
using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.EditorUI.Thumbnails;
using RockEngine.Editor.Selection;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class AssetInspectorWindow : EditorWindow
    {
        private readonly ISelectionManager _selectionManager;
        private readonly PropertyDrawer _propertyDrawer;

        public AssetInspectorWindow(
            ISelectionManager selectionManager,
            IAssetManager assetManager,
            ImGuiController imGuiController,
            IThumbnailService thumbnailService)
            : base("Asset Inspector")
        {
            _selectionManager = selectionManager;
            _propertyDrawer = new PropertyDrawer(assetManager, imGuiController, thumbnailService);

            // Listen for selection changes
            _selectionManager.SelectionChanged += OnSelectionChanged;
        }

        private void OnSelectionChanged(SelectionContext context)
        {
            // Force redraw next frame
            Invalidate();
        }

        protected override void OnDraw()
        {
            var context = _selectionManager.CurrentSelection;

            if (!context.HasSelection)
            {
                ImGui.Text("No object selected");
                return;
            }

            if (context.HasEntitySelection)
            {
                // Handle entity inspector – but that's the job of InspectorWindow.
                // We can optionally show a brief entity info or delegate.
                ImGui.Text("Entity selected – use the main Inspector window.");
                return;
            }

            if (context.HasAssetSelection)
            {
                DrawAssetInspector(context.SelectedAsset!);
            }
        }

        private void DrawAssetInspector(IAsset asset)
        {
            ImGui.Text($"Asset: {asset.Name} ({asset.Type})");

            if (asset is TextureAsset textureAsset)
            {
                DrawTextureAsset(textureAsset);
            }
            else
            {
                ImGui.Text($"Detailed inspector for {asset.GetType().Name} is not yet implemented.");
            }
        }

        private void DrawTextureAsset(TextureAsset textureAsset)
        {
            // Ensure data loaded
            if (!textureAsset.IsDataLoaded)
            {
                if (ImGui.Button("Load Asset Data"))
                {
                    textureAsset.LoadDataAsync().ConfigureAwait(false);
                }
                return;
            }

            var textureData = textureAsset.Data;
            if (textureData == null)
            {
                return;
            }

            // Preview
            if (textureAsset.GpuReady)
            {
                _propertyDrawer.HandleTexturePreview(textureAsset.Texture as Texture2D);
            }
            else
            {
                if (ImGui.Button("Load GPU Resources"))
                {
                    textureAsset.LoadGpuResourcesAsync().AsTask().ContinueWith(_ => Invalidate());
                }
            }

            ImGui.Separator();

            // Import Settings (read-only)
            if (ImGui.CollapsingHeader("Import Settings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var importAccessors = _propertyDrawer.GetAccessorsForType(typeof(TextureData));
                _propertyDrawer.DrawObjectProperties(textureData, importAccessors);

                if (ImGui.Button("Reimport"))
                {
                    textureAsset.UnloadGpuResources();
                    textureAsset.LoadDataAsync().ContinueWith(_ =>
                    {
                        textureAsset.LoadGpuResourcesAsync().AsTask().ContinueWith(_ => Invalidate());
                    });
                }
            }

            // Sampler Settings (editable)
            if (ImGui.CollapsingHeader("Sampler", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var samplerAccessors = _propertyDrawer.GetAccessorsForType(typeof(SamplerState));



                _propertyDrawer.DrawObjectProperties(textureData.Sampler, samplerAccessors);

            }
        }

        public void Invalidate()
        {
            // For a typical ImGui window, it will redraw each frame anyway.
            // This method exists to force immediate redraw if needed.
        }
    }
}