using System.Numerics;
using ImGuiNET;
using RockEngine.Core.ECS;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;
using RockEngine.Editor.Helpers;
using RockEngine.Editor.Selection;
using ZLinq;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class SceneHierarchyWindow : EditorWindow
    {
        private readonly World _world;
        private readonly ISelectionManager _selectionManager;

        // Rename popup state
        private Entity _renamingEntity;
        private string _renameBuffer = "";


        public SceneHierarchyWindow(World world, ISelectionManager selectionManager) : base("Scene Hierarchy")
        {
            _world = world;
            _selectionManager = selectionManager;
            _selectionManager.SelectionContextChanged += OnSelectionContextChanged;
        }

        private void OnSelectionContextChanged(SelectionContext context)
        {
            // Handle if needed
        }

        protected override void OnDraw()
        {
            ApplyWindowStyling();
            ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 16);

            if (ImGui.BeginChild("SceneTree", new Vector2(0, -ImGui.GetFrameHeightWithSpacing())))
            {
                // 1. Get the full available size of the child window
                var windowSize = ImGui.GetContentRegionAvail();
                var cursorPos = ImGui.GetCursorPos();

                // 2. Draw an invisible dummy that fills the entire window (background)
                ImGui.Dummy(windowSize);

                // 3. Make this dummy a drop target for unparenting (root level)
                if (ImGui.BeginDragDropTarget())
                {
                    if (DragDropHelper.AcceptDragDropPayload<Entity>(out var draggedEntity, "ENTITY"))
                    {
                        if (draggedEntity != null && draggedEntity.Parent != null)
                        {
                            var cmd = new ReparentEntityCommand(draggedEntity, null);
                            UndoRedoService.Instance.Execute(cmd);
                        }
                    }
                    ImGui.EndDragDropTarget();
                }

                // 4. Reset cursor to the top to draw the actual hierarchy on top
                ImGui.SetCursorPos(cursorPos);

                // 5. Draw root entities (using a snapshot to avoid collection modified errors)
                var rootEntities = _world.GetEntities().Where(e => e.Parent == null).ToList();
                foreach (var entity in rootEntities)
                {
                    DrawEntityNode(entity);
                }

            }
            ImGui.EndChild();

            // Add Entity button
            if (ImGui.Button("+ Add Entity"))
            {
                var cmd = new CreateEntityCommand(_world, "New Entity");
                UndoRedoService.Instance.Execute(cmd);
            }

            // Rename popup
            if (_renamingEntity != null)
            {
                ImGui.OpenPopup("Rename Entity");
                if (ImGui.BeginPopupModal("Rename Entity", ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.Text($"Rename '{_renamingEntity.Name}':");
                    ImGui.InputText("##rename", ref _renameBuffer, 100);
                    if (ImGui.Button("OK"))
                    {
                        if (!string.IsNullOrEmpty(_renameBuffer) && _renameBuffer != _renamingEntity.Name)
                        {
                            var cmd = new RenameEntityCommand(_renamingEntity, _renamingEntity.Name, _renameBuffer);
                            UndoRedoService.Instance.Execute(cmd);
                        }
                        _renamingEntity = null;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                    {
                        _renamingEntity = null;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }

            ImGui.PopStyleVar();
            PopWindowStyling();
        }

        private void DrawEntityNode(Entity entity)
        {
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (entity.Children.Count == 0)
                flags |= ImGuiTreeNodeFlags.Leaf;

            if (_selectionManager.IsEntitySelected(entity))
                flags |= ImGuiTreeNodeFlags.Selected;

            bool isOpen = ImGui.TreeNodeEx($"{entity.Name}##{entity.ID}", flags);
            HandleEntitySelection(entity);

            DragDropHelper.BeginDragDropSource(entity, "ENTITY");

            if (ImGui.BeginDragDropTarget())
            {
                if (DragDropHelper.AcceptDragDropPayload<Entity>(out var draggedEntity, "ENTITY"))
                {
                    if (draggedEntity != null && CanReparent(draggedEntity, entity))
                    {
                        var cmd = new ReparentEntityCommand(draggedEntity, entity);
                        UndoRedoService.Instance.Execute(cmd);
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // Context menu
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Rename"))
                {
                    _renamingEntity = entity;
                    _renameBuffer = entity.Name;
                }
                if (ImGui.MenuItem("Delete"))
                {
                    var cmd = new DeleteEntityCommand(_world, entity);
                    UndoRedoService.Instance.Execute(cmd);
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Add Child"))
                {
                    var cmd = new CreateChildEntityCommand(_world, entity, "New Child");
                    UndoRedoService.Instance.Execute(cmd);
                }
                ImGui.EndPopup();
            }

            if (isOpen)
            {
                for (int i = 0; i < entity.Children.Count; i++)
                {
                    Entity? child = entity.Children[i];
                    DrawEntityNode(child);
                }
                ImGui.TreePop();
            }
        }

        /// <summary>
        /// Handles dropping onto the root area (empty space) to remove parent.
        /// </summary>
        private void HandleRootDropTarget()
        {
            if (ImGui.BeginDragDropTarget())
            {
                if (DragDropHelper.AcceptDragDropPayload<Entity>(out var draggedEntity, "ENTITY"))
                {
                    if (draggedEntity != null && draggedEntity.Parent != null)
                    {
                        var cmd = new ReparentEntityCommand(draggedEntity, null);
                        UndoRedoService.Instance.Execute(cmd);
                    }
                }
                ImGui.EndDragDropTarget();
            }
        }

        /// <summary>
        /// Checks if reparenting is valid: not the same entity, not a descendant, and not already child.
        /// </summary>
        private bool CanReparent(Entity dragged, Entity newParent)
        {
            if (dragged == null || newParent == null) return false;
            if (dragged == newParent) return false; // Can't parent to itself
            if (IsDescendantOf(newParent, dragged)) return false; // Can't set a descendant as parent (would create cycle)
            if (dragged.Parent == newParent) return false; // Already child
            return true;
        }

        /// <summary>
        /// Checks if 'entity' is a descendant of 'potentialAncestor'.
        /// </summary>
        private bool IsDescendantOf(Entity entity, Entity potentialAncestor)
        {
            var current = entity.Parent;
            while (current != null)
            {
                if (current == potentialAncestor)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        private void HandleEntitySelection(Entity entity)
        {
            if (ImGui.IsItemClicked())
            {
                var io = ImGui.GetIO();
                if (io.KeyCtrl)
                {
                    if (_selectionManager.IsEntitySelected(entity))
                        _selectionManager.RemoveFromSelection(entity, SelectionSource.SceneHierarchy);
                    else
                        _selectionManager.AddToSelection(entity, SelectionSource.SceneHierarchy);
                }
                else if (io.KeyShift)
                {
                    PerformRangeSelection(entity);
                }
                else
                {
                    _selectionManager.SelectEntity(entity, SelectionSource.SceneHierarchy);
                }
            }
        }

        private void PerformRangeSelection(Entity targetEntity)
        {
            var rootEntities = _world.GetEntities().Where(e => e.Parent == null).ToList();
            var currentSelection = _selectionManager.CurrentSelection;

            if (currentSelection.PrimaryEntity != null)
            {
                int startIndex = rootEntities.IndexOf(currentSelection.PrimaryEntity);
                int endIndex = rootEntities.IndexOf(targetEntity);
                if (startIndex >= 0 && endIndex >= 0)
                {
                    int rangeStart = Math.Min(startIndex, endIndex);
                    int rangeEnd = Math.Max(startIndex, endIndex);
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