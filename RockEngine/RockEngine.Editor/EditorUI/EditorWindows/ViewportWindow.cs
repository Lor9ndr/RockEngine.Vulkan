using ImGuiNET;

using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Editor.EditorComponents;
using RockEngine.Editor.EditorUI.ImGuiRendering;
using RockEngine.Editor.Rendering.Buffers;
using RockEngine.Editor.Rendering.Passes;
using RockEngine.Editor.Rendering.RenderTargets;
using RockEngine.Editor.Selection;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using System.Numerics;
using System.Threading.Tasks;

using ZLinq;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class ViewportWindow : EditorWindow
    {
        private readonly World _world;
        private readonly IInputContext _inputContext;
        private readonly ImGuiController _imGuiController;
        private readonly PickingBuffer _pickingBuffer;
        private readonly PickingRenderTarget _pickingRenderTarget;
        private readonly ISelectionManager _selectionManager;
        private readonly bool _isSceneView;

        private Vector2 _currentSize;
        private Vector2 _currentImageMin;
        private Vector2 _currentImageMax;

        private bool _isGizmoDragging = false;
        private GizmoAxis _currentGizmoSelection = GizmoAxis.None;

        private TransformGizmo Gizmo => _world.GetEntitiesWithComponent<TransformGizmo>()
            .FirstOrDefault()?.GetComponent<TransformGizmo>();

        public ViewportWindow(string title, World world, IInputContext inputContext, ImGuiController imGuiController)
            : this(title, world, inputContext, imGuiController, null) { }

        public ViewportWindow(string title, World world, IInputContext inputContext,
            ImGuiController imGuiController, ISelectionManager selectionManager) : base(title)
        {
            _world = world;
            _inputContext = inputContext;
            _imGuiController = imGuiController;
            _selectionManager = selectionManager;
            _isSceneView = title == "Scene View";
            _pickingBuffer = new PickingBuffer(VulkanContext.GetCurrent());
            _pickingRenderTarget =  IoC.Container.GetInstance<PickingPassStrategy>()
                    .PickingRenderTarget;
        }

        public override void Draw()
        {
            if (!IsOpen)
            {
                return;
            }

            if (ImGui.Begin(Title, ref _isOpen))
            {
                OnDraw();
            }
            ImGui.End();
        }

        protected override void OnDraw()
        {
            var camera = GetCamera();
            if (camera == null)
            {
                return;
            }

            DrawRenderTarget(camera.RenderTarget, ref _currentSize);

            if (_isSceneView && camera is DebugCamera debugCam)
            {
                HandleViewportInteraction(debugCam);
            }
        }

        public void Update()
        {
            var camera = GetCamera();
            if (camera == null)
            {
                return;
            }

            _currentSize.X = Math.Max(_currentSize.X, 1);
            _currentSize.Y = Math.Max(_currentSize.Y, 1);

            camera.RenderTarget?.Resize(new Extent2D((uint)_currentSize.X, (uint)_currentSize.Y));

            if (_isSceneView)
            {
                _pickingRenderTarget.Resize(new Extent2D((uint)_currentSize.X, (uint)_currentSize.Y));
                UpdateGizmoPosition();
            }
        }

        private void UpdateGizmoPosition()
        {
            var selectedEntity = _selectionManager?.CurrentSelection?.PrimaryEntity;
            var gizmo = Gizmo;

            if (selectedEntity != null && gizmo != null)
            {
                gizmo.Entity.SetActive(true);
                gizmo.Entity.Transform.Position = selectedEntity.Transform.WorldPosition;

                // Scale gizmo based on distance from camera
                var camera = GetCamera();
                if (camera != null)
                {
                    float distance = Vector3.Distance(camera.Entity.Transform.Position, selectedEntity.Transform.WorldPosition);
                    gizmo.Entity.Transform.Scale = new Vector3(distance * 0.2f);
                }
            }
            else
            {
                gizmo?.Entity.SetActive(false);
            }
        }

        private Camera GetCamera()
        {
            var entities = _world.GetEntities();

            if (_isSceneView)
            {
                return entities.FirstOrDefault(e => e.GetComponent<DebugCamera>() != null)
                    ?.GetComponent<DebugCamera>();
            }
            else
            {
                return entities.FirstOrDefault(e =>
                    e.GetComponent<Camera>() != null && e.GetComponent<DebugCamera>() == null)
                    ?.GetComponent<Camera>();
            }
        }

        private void HandleViewportInteraction(DebugCamera debugCam)
        {
            var windowHovered = ImGui.IsWindowHovered();
            var mouse = _inputContext.Mice[0];

            // Update cursor
            mouse.Cursor.StandardCursor = windowHovered ? StandardCursor.Hand : StandardCursor.Arrow;

            if (windowHovered)
            {
                HandleGizmoModeSwitching();
            }

            // Handle camera movement (only when not dragging gizmo)
            if (!_isGizmoDragging && (ImGui.IsMouseDragging(ImGuiMouseButton.Right) ||
                (ImGui.IsMouseDown(ImGuiMouseButton.Right) && ImGui.IsKeyDown(ImGuiKey.ModAlt))))
            {
                debugCam.CanMove = windowHovered;
            }
            else
            {
                debugCam.CanMove = false;
            }

            var viewportCoords = GetViewportMouseCoordinates();

            // Handle gizmo hover feedback
            if (windowHovered && viewportCoords.HasValue && !_isGizmoDragging)
            {
                HandleGizmoHover(viewportCoords.Value);
            }

            // Handle picking and dragging
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && windowHovered && IsMouseInViewportImage())
            {
                if (viewportCoords.HasValue)
                {
                    HandleMouseClick(viewportCoords.Value);
                }
            }

            HandleGizmoDragging(debugCam);

            // Show context menu
            if (!_isGizmoDragging && !ImGui.IsMouseDragging(ImGuiMouseButton.Right) &&
                ImGui.IsMouseClicked(ImGuiMouseButton.Right) && windowHovered && IsMouseInViewportImage())
            {
                ShowViewportContextMenu();
            }
        }

        private void HandleMouseClick(Vector2 viewportCoords)
        {
            var gizmoAxis = PickGizmoAtPosition(viewportCoords);
            var gizmo = Gizmo;

            if (gizmoAxis != GizmoAxis.None && gizmo != null)
            {
                _currentGizmoSelection = gizmoAxis;
                gizmo.SetSelectedAxis(gizmoAxis);
                gizmo.StartDrag(ImGui.GetMousePos());
                _isGizmoDragging = true;
            }
            else
            {
                var additive = ImGui.GetIO().KeyCtrl;
                PickEntityAtPosition(viewportCoords, additive);
            }
        }

        private void HandleGizmoDragging(DebugCamera debugCam)
        {
            if (_isGizmoDragging && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var selectedEntity = _selectionManager?.CurrentSelection?.PrimaryEntity;
                if (selectedEntity != null && Gizmo != null)
                {
                    Gizmo.UpdateDrag(ImGui.GetMousePos(), debugCam, selectedEntity, Size);
                }
            }

            // End gizmo drag
            if (_isGizmoDragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _isGizmoDragging = false;
                _currentGizmoSelection = GizmoAxis.None;
                Gizmo?.EndDrag();
                Gizmo?.SetSelectedAxis(GizmoAxis.None);
            }
        }

        private bool IsMouseInViewportImage()
        {
            if (!ImGui.IsWindowHovered())
            {
                return false;
            }

            var mousePos = ImGui.GetMousePos();
            return mousePos.X >= _currentImageMin.X && mousePos.X <= _currentImageMax.X &&
                   mousePos.Y >= _currentImageMin.Y && mousePos.Y <= _currentImageMax.Y;
        }

        private void HandleGizmoModeSwitching()
        {
            if (ImGui.IsKeyPressed(ImGuiKey.T) || ImGui.IsKeyPressed(ImGuiKey.Keypad1))
            {
                SetGizmoMode(GizmoType.Translate);
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.R) || ImGui.IsKeyPressed(ImGuiKey.Keypad2))
            {
                SetGizmoMode(GizmoType.Rotate);
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.E) || ImGui.IsKeyPressed(ImGuiKey.Keypad3))
            {
                SetGizmoMode(GizmoType.Scale);
            }
        }

        private void SetGizmoMode(GizmoType mode)
        {
            Gizmo?.CurrentMode = mode;
        }

        private void HandleGizmoHover(Vector2 viewportCoords)
        {
            // Not performance to read hovering of gizmo for now
            //var gizmoAxis = PickGizmoAtPosition(viewportCoords);
            //Gizmo?.SetHoveredAxis(gizmoAxis);
        }

        private GizmoAxis PickGizmoAtPosition(Vector2 viewportCoords)
        {
            try
            {
                var pickingPass = IoC.Container.GetInstance<PickingPassStrategy>();
                var pixelColor = _pickingBuffer.ReadPixel(
                    pickingPass.PickingRenderTarget.OutputTexture,
                    (uint)viewportCoords.X, (uint)viewportCoords.Y, false);

                var handleId = ColorToGizmoHandleId(pixelColor);
                return DecodeGizmoAxis(handleId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gizmo picking failed: {ex.Message}");
                return GizmoAxis.None;
            }
        }

        private uint ColorToGizmoHandleId(Vector4 color)
        {
            var r = (uint)Math.Round(color.X * 255.0f);
            var g = (uint)Math.Round(color.Y * 255.0f);
            var b = (uint)Math.Round(color.Z * 255.0f);
            var a = (uint)Math.Round(color.W * 255.0f);

            return (a << 24) | (b << 16) | (g << 8) | r;
        }

        private GizmoAxis DecodeGizmoAxis(uint handleId)
        {
            // Check if this is a gizmo (high byte = 0xFF)
            if ((handleId >> 24) != 0xFF)
            {
                return GizmoAxis.None;
            }

            // Extract axis mask from the third byte
            uint axisMask = (handleId >> 8) & 0xFF;

            return axisMask switch
            {
                1 => GizmoAxis.X,
                2 => GizmoAxis.Y,
                4 => GizmoAxis.Z,
                8 => GizmoAxis.Uniform,
                16 => GizmoAxis.View,
                _ => GizmoAxis.None
            };
        }

        private void ShowViewportContextMenu()
        {
            if (ImGui.BeginPopupContextWindow("ViewportContextMenu"))
            {
                if (ImGui.BeginMenu("Gizmo Mode"))
                {
                    var gizmo = Gizmo;
                    if (ImGui.MenuItem("Translate", "G", gizmo?.CurrentMode == GizmoType.Translate))
                    {
                        SetGizmoMode(GizmoType.Translate);
                    }

                    if (ImGui.MenuItem("Rotate", "R", gizmo?.CurrentMode == GizmoType.Rotate))
                    {
                        SetGizmoMode(GizmoType.Rotate);
                    }

                    if (ImGui.MenuItem("Scale", "S", gizmo?.CurrentMode == GizmoType.Scale))
                    {
                        SetGizmoMode(GizmoType.Scale);
                    }

                    ImGui.EndMenu();
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Select All")) { /* TODO */ }
                if (ImGui.MenuItem("Deselect All"))
                {
                    _selectionManager?.ClearSelection();
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Create Empty Entity")) { /* TODO */ }

                ImGui.EndPopup();
            }
        }

        private Vector2? GetViewportMouseCoordinates()
        {
            try
            {
                if (!IsMouseInViewportImage())
                {
                    return null;
                }

                var mousePos = ImGui.GetMousePos();
                var imageSize = _currentImageMax - _currentImageMin;

                if (imageSize.X <= 0 || imageSize.Y <= 0)
                {
                    return null;
                }

                var relativePos = mousePos - _currentImageMin;
                relativePos.X = Math.Clamp(relativePos.X, 0, imageSize.X - 1);
                relativePos.Y = Math.Clamp(relativePos.Y, 0, imageSize.Y - 1);

                var camera = GetCamera();
                var texture = camera?.RenderTarget?.OutputTexture;
                if (texture == null || texture.Width == 0 || texture.Height == 0)
                {
                    return null;
                }

                var uv = new Vector2(relativePos.X / imageSize.X, relativePos.Y / imageSize.Y);
                var pixelCoords = new Vector2(uv.X * (texture.Width - 1), uv.Y * (texture.Height - 1));

                pixelCoords.X = Math.Clamp(pixelCoords.X, 0, texture.Width - 1);
                pixelCoords.Y = Math.Clamp(pixelCoords.Y, 0, texture.Height - 1);

                return pixelCoords;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting viewport coordinates: {ex.Message}");
                return null;
            }
        }

        private void PickEntityAtPosition(Vector2 viewportCoords, bool additive)
        {
            try
            {
                var pickingPass = IoC.Container.GetInstance<PickingPassStrategy>();
                var pixelColor = _pickingBuffer.ReadPixel(
                    pickingPass.PickingRenderTarget.OutputTexture,
                    (uint)viewportCoords.X, (uint)viewportCoords.Y, false);

                var entityId = ColorToEntityId(pixelColor);

                if (entityId > 0)
                {
                    var entity = FindEntityById(entityId);
                    if (entity != null && entity.GetComponent<TransformGizmo>() is null &&
                        _selectionManager?.CanSelectEntity(entity) == true)
                    {
                        HandleEntitySelection(entity, additive);
                    }
                }
                else if (!additive)
                {
                    _selectionManager?.ClearSelection(SelectionSource.ViewportPicking);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Picking failed: {ex.Message}");
            }
        }

        private void HandleEntitySelection(Entity entity, bool additive)
        {
            if (additive)
            {
                if (_selectionManager.IsEntitySelected(entity))
                {
                    _selectionManager.RemoveFromSelection(entity, SelectionSource.ViewportPicking);
                }
                else
                {
                    _selectionManager.AddToSelection(entity, SelectionSource.ViewportPicking);
                }
            }
            else
            {
                _selectionManager.SelectEntity(entity, SelectionSource.ViewportPicking);
            }
        }

        private uint ColorToEntityId(Vector4 color)
        {
            var r = (uint)(color.X * 255);
            var g = (uint)(color.Y * 255);
            var b = (uint)(color.Z * 255);
            var a = (uint)(color.W * 255);

            return (a << 24) | (b << 16) | (g << 8) | r;
        }

        private Entity FindEntityById(uint entityId)
        {
            return _world.GetEntities().FirstOrDefault(e => e.ID == entityId);
        }

        private void DrawRenderTarget(RenderTarget renderTarget, ref Vector2 currentSize)
        {
            if (renderTarget == null)
            {
                return;
            }

            var texId = _imGuiController.GetTextureID(renderTarget.OutputTexture);
            var availableSize = ImGui.GetContentRegionAvail();

            ImGui.Image(texId, availableSize);

            _currentImageMin = ImGui.GetItemRectMin();
            _currentImageMax = ImGui.GetItemRectMax();

            DrawGizmoModeIndicator();
            currentSize = availableSize;
        }

        private void DrawGizmoModeIndicator()
        {
            var gizmo = Gizmo;
            if (gizmo == null)
            {
                return;
            }

            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();
            var textPos = new Vector2(windowPos.X + 10, windowPos.Y + 40);

            string modeText = gizmo.CurrentMode switch
            {
                GizmoType.Translate => "Translate (G)",
                GizmoType.Rotate => "Rotate (R)",
                GizmoType.Scale => "Scale (S)",
                _ => "Unknown"
            };

            var textSize = ImGui.CalcTextSize(modeText);
            var bgMin = textPos;
            var bgMax = textPos + textSize + new Vector2(5, 5);

            drawList.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.7f)), 5);
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), modeText);
        }
    }
}