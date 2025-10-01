// RockEngine.Editor/EditorUI/Windows/SceneHierarchyWindow.cs
using ImGuiNET;

using RockEngine.Core.ECS;

using System.Numerics;

using ZLinq;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class SceneHierarchyWindow : EditorWindow
    {
        private readonly World _world;
        private Entity _selectedEntity;

        public event Action<Entity> SelectedEntityChanged;

        public Entity SelectedEntity
        {
            get => _selectedEntity;
            set
            {
                if (_selectedEntity != value)
                {
                    _selectedEntity = value;
                    SelectedEntityChanged?.Invoke(value);
                }
            }
        }

        public SceneHierarchyWindow(World world) : base("Scene Hierarchy")
        {
            _world = world;
        }

        protected override void OnDraw()
        {
            ApplyWindowStyling();

            ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 16);

            if (ImGui.BeginChild("SceneTree", new Vector2(0, -ImGui.GetFrameHeightWithSpacing())))
            {
                foreach (var entity in _world.GetEntities().Where(e => e.Parent == null))
                {
                    DrawEntityNode(entity);
                }
            }
            ImGui.EndChild();

            if (ImGui.Button("+ Add Entity"))
            {
                // Add entity logic
            }

            ImGui.PopStyleVar();
            PopWindowStyling();
        }

        private void DrawEntityNode(Entity entity)
        {
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (entity.Children.Count == 0)
                flags |= ImGuiTreeNodeFlags.Leaf;

            if (_selectedEntity == entity)
                flags |= ImGuiTreeNodeFlags.Selected;

            bool isOpen = ImGui.TreeNodeEx($"{entity.Name}##{entity.ID}", flags);

            if (ImGui.IsItemClicked())
            {
                SelectedEntity = entity;
            }

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Rename")) { }
                if (ImGui.MenuItem("Delete")) { }
                ImGui.Separator();
                if (ImGui.MenuItem("Add Child")) { }
                ImGui.EndPopup();
            }

            if (isOpen)
            {
                foreach (var child in entity.Children)
                    DrawEntityNode(child);
                ImGui.TreePop();
            }
        }
    }
}