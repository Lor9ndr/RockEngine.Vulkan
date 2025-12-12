using ImGuiNET;

using RockEngine.Core.ECS;
using RockEngine.Editor.Selection;

using System.Numerics;

using ZLinq;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class SceneHierarchyWindow : EditorWindow
    {
        private readonly World _world;

        private readonly ISelectionManager _selectionManager;

        public SceneHierarchyWindow(World world, ISelectionManager selectionManager) : base("Scene Hierarchy")
        {
            _world = world;
            _selectionManager = selectionManager;
            _selectionManager.SelectionContextChanged += OnSelectionContextChanged;
        }

        private void OnSelectionContextChanged(SelectionContext context)
        {
            // Auto-expand hierarchy to show selected entities
            if (context.Source != SelectionSource.SceneHierarchy && context.PrimaryEntity != null)
            {
                // You would implement tree expansion logic here
            }
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
            {
                flags |= ImGuiTreeNodeFlags.Leaf;
            }

            if (_selectionManager.IsEntitySelected(entity))
            {
                flags |= ImGuiTreeNodeFlags.Selected;
            }

            bool isOpen = ImGui.TreeNodeEx($"{entity.Name}##{entity.ID}", flags);

            HandleEntitySelection(entity);

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
                {
                    DrawEntityNode(child);
                }

                ImGui.TreePop();
            }
        }
        private void HandleEntitySelection(Entity entity)
        {
            if (ImGui.IsItemClicked())
            {
                var io = ImGui.GetIO();

                if (io.KeyCtrl)
                {
                    // Additive selection
                    if (_selectionManager.IsEntitySelected(entity))
                    {
                        _selectionManager.RemoveFromSelection(entity, SelectionSource.SceneHierarchy);
                    }
                    else
                    {
                        _selectionManager.AddToSelection(entity, SelectionSource.SceneHierarchy);
                    }
                }
                else if (io.KeyShift)
                {
                    // Range selection
                    PerformRangeSelection(entity);
                }
                else
                {
                    // Single selection
                    _selectionManager.SelectEntity(entity, SelectionSource.SceneHierarchy);
                }
            }
        }
        

       
        private void PerformRangeSelection(Entity targetEntity)
        {
            // Get all root entities for range selection
            var rootEntities = _world.GetEntities().Where(e => e.Parent == null).ToList();
            var currentSelection = _selectionManager.CurrentSelection;

            if (currentSelection.PrimaryEntity != null)
            {
                var startIndex = rootEntities.IndexOf(currentSelection.PrimaryEntity);
                var endIndex = rootEntities.IndexOf(targetEntity);

                if (startIndex >= 0 && endIndex >= 0)
                {
                    var rangeStart = Math.Min(startIndex, endIndex);
                    var rangeEnd = Math.Max(startIndex, endIndex);
                    var rangeEntities = rootEntities.Skip(rangeStart).Take(rangeEnd - rangeStart + 1);

                    _selectionManager.SelectEntities(rangeEntities, SelectionSource.SceneHierarchy);
                }
            }
            else
            {
                _selectionManager.SelectEntity(targetEntity, SelectionSource.SceneHierarchy);
            }
        }
    }
}