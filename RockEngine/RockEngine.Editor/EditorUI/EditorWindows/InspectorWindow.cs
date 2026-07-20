using ImGuiNET;

using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.EditorUI.Thumbnails;
using RockEngine.Editor.Selection;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class InspectorWindow : EditorWindow
    {
        private readonly PropertyDrawer _propertyDrawer;
        private readonly ISelectionManager _selectionManager;

        public InspectorWindow(IAssetManager assetManager, ImGuiController imGuiController, ISelectionManager selectionManager, IThumbnailService thumbnailService) : base("Inspector")
        {
            _propertyDrawer = new PropertyDrawer(assetManager, imGuiController, thumbnailService);
            _selectionManager = selectionManager;
        }

        protected override void OnDraw()
        {
            var ctx = _selectionManager.CurrentSelection;
            if (ctx == null || !ctx.HasSelection)
            {
                ImGui.Text("No object selected");
                return;
            }

            ApplyWindowStyling();

            if (ctx.HasEntitySelection)
            {
                DrawEntityInspector(ctx);
            }
            else if (ctx.HasAssetSelection)
            {
                DrawAssetInspector(ctx);
            }

            PopWindowStyling();
        }

        private void DrawEntityInspector(SelectionContext ctx)
        {
            // Entity name and transform
            ImGui.TextDisabled("Entity");
            ImGui.Separator();

            // Transform component
            var transform = ctx.PrimaryEntity.Transform;
            //DrawTransformComponent(transform);

            // Other components
            foreach (var component in ctx.PrimaryEntity.Components.ToArray())
            {
                DrawComponent(component);
            }

            // Add component button
            ImGui.Separator();
            if (ImGui.Button("+ Add Component"))
            {
                ImGui.OpenPopup("AddComponentPopup");
            }

            if (ImGui.BeginPopup("AddComponentPopup"))
            {
                var registrations = IoC.Container.GetCurrentRegistrations().Where(s => s.ImplementationType.GetInterface(nameof(IComponent)) is not null);

                foreach (var registration in registrations)
                {
                    if (ImGui.MenuItem(registration.ImplementationType.Name))
                    {
                        _selectionManager.CurrentSelection.PrimaryEntity.AddComponent(registration.ImplementationType);
                    }
                }
                ImGui.EndPopup();
            }
        }
        private void DrawAssetInspector(SelectionContext ctx)
        {
            var asset = ctx.SelectedAsset;
            if (asset == null)
            {
                return;
            }

            ImGui.TextDisabled("Asset");
            ImGui.Separator();
            ImGui.Text($"Name: {asset.Name}");
            ImGui.Text($"Type: {asset.Type}");

            if (asset is TextureAsset textureAsset)
            {
                DrawTextureAsset(textureAsset);
            }
            else
            {
                ImGui.Text("Detailed inspector not available for this asset type.");
            }
        }

        private void DrawTextureAsset(TextureAsset textureAsset)
        {
            if (!textureAsset.IsDataLoaded)
            {
                if (ImGui.Button("Load Asset Data"))
                {
                    _ = textureAsset.LoadDataAsync();
                }

                return;
            }

            var data = textureAsset.Data!;

            // Preview
            if (textureAsset.GpuReady && textureAsset.Texture is Texture2D texture2D)
            {
                _propertyDrawer.HandleTexturePreview(texture2D);
            }
            else if (ImGui.Button("Load GPU Resources"))
            {
                _ = textureAsset.LoadGpuResourcesAsync().AsTask();
            }

            ImGui.Separator();

            // Import Settings (read-only via handler)
            if (ImGui.CollapsingHeader("Import Settings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var dummyAccessor = new UIPropertyAccessor(
                    name: "TextureData",
                    displayName: "Import Settings",
                    propertyType: typeof(TextureData),
                    getValue: _ => data,
                    setValue: null,
                    canWrite: false,
                    attributes: Array.Empty<Attribute>()
                );
                _propertyDrawer.DrawProperty(textureAsset, dummyAccessor);
            }

            // Sampler Settings (editable via handler)
            if (ImGui.CollapsingHeader("Sampler", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var dummyAccessor = new UIPropertyAccessor(
                    name: "SamplerState",
                    displayName: "Sampler",
                    propertyType: typeof(SamplerState),
                    getValue: _ => data.Sampler,
                    setValue: (value,s) => data.Sampler = (SamplerState)s, // SamplerState is a struct; handler sets fields directly
                    canWrite: true, // not used by the handler
                    attributes: Array.Empty<Attribute>()
                );
                _propertyDrawer.DrawProperty(data, dummyAccessor);
            }
        }

        private void DrawComponent(IComponent component)
        {
            var typeName = component.GetType().Name;
            var isOpen = ImGui.CollapsingHeader(typeName, ImGuiTreeNodeFlags.DefaultOpen);

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Remove Component"))
                {
                    component.Entity.RemoveComponent(component);
                }
                ImGui.EndPopup();
            }

            if (isOpen)
            {
                ImGui.Indent();
                _propertyDrawer.DrawComponentProperties(component);
                ImGui.Unindent();
            }
        }
    }
}