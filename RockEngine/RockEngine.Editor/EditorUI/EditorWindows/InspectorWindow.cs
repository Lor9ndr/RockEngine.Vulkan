using ImGuiNET;

using RockEngine.Core.Assets;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Editor.EditorUI.ImGuiRendering;

using System.Reflection;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class InspectorWindow : EditorWindow
    {
        private Entity _selectedEntity;
        private readonly PropertyDrawer _propertyDrawer;

        public Entity SelectedEntity
        {
            get => _selectedEntity;
            set => _selectedEntity = value;
        }

        public InspectorWindow(AssetManager assetManager, ImGuiController imGuiController) : base("Inspector")
        {
            _propertyDrawer = new PropertyDrawer(assetManager, imGuiController);
        }

        protected override void OnDraw()
        {
            if (_selectedEntity == null)
            {
                ImGui.Text("No entity selected");
                return;
            }

            ApplyWindowStyling();

            // Entity name and transform
            ImGui.TextDisabled("Entity");
            ImGui.Separator();

            // Transform component
            var transform = _selectedEntity.Transform;
            DrawTransformComponent(transform);

            // Other components
            foreach (var component in _selectedEntity.Components.Where(c => c is not Transform))
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
                if (ImGui.MenuItem("Mesh Renderer")) { }
                if (ImGui.MenuItem("Light")) { }
                if (ImGui.MenuItem("Camera")) { }
                ImGui.EndPopup();
            }

            PopWindowStyling();
        }

        private void DrawTransformComponent(Transform transform)
        {
            if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var position = transform.Position;
                var rotation = transform.Rotation;
                var scale = transform.Scale;

                bool changed = false;
                changed |= ImGui.DragFloat3("Position", ref position, 0.1f);
                changed |= ImGui.DragFloat3("Scale", ref scale, 0.1f);

                if (changed)
                {
                    transform.Position = position;
                    transform.Rotation = rotation;
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
                DrawComponentProperties(component);
                ImGui.Unindent();
            }
        }

        private void DrawComponentProperties(IComponent component)
        {
            var properties = component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (!property.CanRead) continue;

                _propertyDrawer.DrawProperty(component, property);
            }
        }
    }
}