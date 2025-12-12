using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.Selection;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class InspectorWindow : EditorWindow
    {
        private readonly PropertyDrawer _propertyDrawer;
        private readonly ISelectionManager _selectionManager;

        public InspectorWindow(AssetManager assetManager, ImGuiController imGuiController, ISelectionManager selectionManager) : base("Inspector")
        {
            _propertyDrawer = new PropertyDrawer(assetManager, imGuiController);
            _selectionManager = selectionManager;
        }

        protected override void OnDraw()
        {
            if (_selectionManager.CurrentSelection == null || !_selectionManager.CurrentSelection.HasSelection)
            {
                ImGui.Text("No entity selected");
                return;
            }

            ApplyWindowStyling();

            // Entity name and transform
            ImGui.TextDisabled("Entity");
            ImGui.Separator();

            // Transform component
            var transform = _selectionManager.CurrentSelection.PrimaryEntity.Transform;
            DrawTransformComponent(transform);

            // Other components
            foreach (var component in _selectionManager.CurrentSelection.PrimaryEntity.Components.Except([transform]))
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
                var registrations = IoC.Container.GetCurrentRegistrations().Where(s=>s.ImplementationType.GetInterface(nameof(IComponent)) is not null);

                foreach (var registration in registrations)
                {
                    if (ImGui.MenuItem(registration.ImplementationType.Name))
                    {
                        _selectionManager.CurrentSelection.PrimaryEntity.AddComponent(registration.ImplementationType);
                    }
                }
                ImGui.EndPopup();
            }

            PopWindowStyling();
        }

        private void DrawTransformComponent(Transform transform)
        {
            if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var position = transform.Position;
                var rotation = transform.EulerAngles;
                var scale = transform.Scale;

                bool changed = false;
                changed |= ImGui.DragFloat3("Position", ref position, 0.1f);
                changed |= ImGui.DragFloat3("Rotation", ref rotation, 0.1f);
                changed |= ImGui.DragFloat3("Scale", ref scale, 0.1f);

                if (changed)
                {
                    transform.Position = position;
                    transform.EulerAngles = rotation;
                    transform.Scale = scale;
                }
            }
        }

        private void DrawComponent(IComponent component)
        {
            var typeName = component.GetType().Name;
            var isOpen = ImGui.CollapsingHeader(typeName, ImGuiTreeNodeFlags.DefaultOpen);

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Remove Component")) { }
                if (ImGui.MenuItem("Reset")) { }
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