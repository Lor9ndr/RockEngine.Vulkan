using ImGuiNET;

using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

using System.Numerics;

namespace RockEngine.Editor.Layers
{
    public class TitleBarLayer : ILayer
    {
        private const int TitleBarHeight = 40;
        private readonly IWindow _window;
        private readonly IInputContext _inputContext;
        private Texture _iconTexture;
        private bool _isDragging;
        private Vector2D<int> _dragStartWindowPos;
        private Vector2D<float> _dragStartMousePos;
        private bool _isHoveringButton;
        private const float RESIZE_BORDER = 4f;
        private Vector2D<int> _resizeStartSize;
        private Vector2D<float> _resizeStartMouse;
        private bool _isResizing;
        private bool _isHoveringTitleBar;
        private Vector2D<int> _restorePosition;
        private Vector2D<int> _restoreSize;
        private bool _wasMaximized;

        public TitleBarLayer(IWindow window, IInputContext inputContext, Texture iconTexture = null)
        {
            _window = window;
            _inputContext = inputContext;
            _iconTexture = iconTexture;
            var mouse = _inputContext.Mice[0];
            mouse.MouseDown += (s, e) =>
            {
                var mousePos = GetAbsoluteMousePosition(mouse);
                if (e == MouseButton.Left && !_isHoveringButton && _isHoveringTitleBar)
                {

                    if (IsInTitleBarArea(mousePos) && !IsOnResizeArea(mousePos))
                    {
                        _isDragging = true;
                        _dragStartMousePos = mousePos;
                        _dragStartWindowPos = _window.Position;
                    }
                }

            if (_isDragging) return;

                if (e == MouseButton.Left )
                {
                    if (!_isResizing && IsOnResizeArea(mousePos))
                    {
                        _isResizing = true;
                        _resizeStartMouse = mousePos;
                        _resizeStartSize = _window.Size;
                    }
                }
                

                if (_isResizing)
                {
                    var delta = mousePos - _resizeStartMouse;
                    var newSize = new Vector2D<int>(
                        Math.Max(100, (int)(_resizeStartSize.X + delta.X)),
                        Math.Max(100, (int)(_resizeStartSize.Y + delta.Y))
                    );

                    _window.Size = newSize;
                    // Update start position for smooth continuous resizing
                    _resizeStartMouse = mousePos;
                    _resizeStartSize = newSize;
                }
            };

            mouse.MouseUp += (s, e) =>
            {
                if (e == MouseButton.Left)
                {
                    _isDragging = false;
                    _isResizing = false;
                }
            };
        }
        private void HandleDragging(Vector2D<float> screenMousePos)
        {
            if (_isResizing) return; // Prevent dragging while resizing

            if (_isDragging)
            {
                var delta = screenMousePos - _dragStartMousePos;
                _window.Position = new Vector2D<int>(
                    (int)(_dragStartWindowPos.X + delta.X),
                    (int)(_dragStartWindowPos.Y + delta.Y)
                );
                // Update start position for smooth continuous dragging
                _dragStartMousePos = screenMousePos;
                _dragStartWindowPos = _window.Position;
            }
        }
      

        public void OnUpdate()
        {
            var mouse = _inputContext.Mice[0];
            var mousePos = GetAbsoluteMousePosition(mouse);

            HandleDragging(mousePos);
            HandleResizing(mouse, mousePos);
            UpdateCursor(mousePos, mouse);
        }

        private Vector2D<float> GetAbsoluteMousePosition(IMouse mouse)
        {
            var windowPos = _window.Position;
            // Mouse position is relative to window, convert to screen space
            return new Vector2D<float>(
                windowPos.X + mouse.Position.X,
                windowPos.Y + mouse.Position.Y
            );
        }

     

        private void HandleResizing(IMouse mouse, Vector2D<float> screenMousePos)
        {

            if (_isDragging) return;
            if (_isResizing)
            {
                var delta = screenMousePos - _resizeStartMouse;
                var newSize = new Vector2D<int>(
                    Math.Max(100, (int)(_resizeStartSize.X + delta.X)),
                    Math.Max(100, (int)(_resizeStartSize.Y + delta.Y))
                );

                _window.Size = newSize;
                // Update start position for smooth continuous resizing
                _resizeStartMouse = screenMousePos;
                _resizeStartSize = newSize;
            }
        }

        private bool IsInTitleBarArea(Vector2D<float> screenMousePos)
        {
            var windowPos = _window.Position;
            return screenMousePos.Y >= windowPos.Y &&
                   screenMousePos.Y <= windowPos.Y + TitleBarHeight &&
                   screenMousePos.X >= windowPos.X &&
                   screenMousePos.X <= windowPos.X + _window.Size.X;
        }

        private bool IsOnResizeArea(Vector2D<float> mousePos)
        {
            var windowPos = _window.Position;
            var windowSize = _window.Size;

            // Check right edge (within 4px of right border)
            bool rightEdge = mousePos.X >= windowPos.X + windowSize.X - RESIZE_BORDER &&
                            mousePos.X <= windowPos.X + windowSize.X;

            // Check bottom edge (within 4px of bottom border)
            bool bottomEdge = mousePos.Y >= windowPos.Y + windowSize.Y - RESIZE_BORDER &&
                             mousePos.Y <= windowPos.Y + windowSize.Y;

            return rightEdge || bottomEdge;
        }
        private void UpdateCursor(Vector2D<float> screenMousePos, IMouse mouse)
        {
            if (IsOnResizeArea(screenMousePos))
            {
                var windowPos = _window.Position;
                var windowSize = _window.Size;

                bool rightEdge = screenMousePos.X >= windowPos.X + windowSize.X - RESIZE_BORDER;
                bool bottomEdge = screenMousePos.Y >= windowPos.Y + windowSize.Y - RESIZE_BORDER;

                if (rightEdge && bottomEdge)
                    mouse.Cursor.StandardCursor = StandardCursor.ResizeAll;
                else if (rightEdge)
                    mouse.Cursor.StandardCursor = StandardCursor.HResize;
                else if (bottomEdge)
                    mouse.Cursor.StandardCursor = StandardCursor.VResize;
            }
            else
            {
                mouse.Cursor.StandardCursor = StandardCursor.Arrow;
            }
        }
        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            _isHoveringButton = false;

            ImGui.SetNextWindowPos(new Vector2(0, 0));
            ImGui.SetNextWindowSize(new Vector2(_window.Size.X, 40));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f);
            if( ImGui.Begin("TitleBar",
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings))
            {
                _isHoveringTitleBar = ImGui.IsWindowHovered();

                // Draw icon
                /* if (_iconTexture != null)
                 {
                     ImGui.Image((IntPtr)_iconTexture.Handle, new Vector2(24, 24));
                     ImGui.SameLine();
                 }*/

                // Title text
                ImGui.Text(_window.Title);

                // Buttons on the right
                float buttonSize = 24;
                ImGui.SameLine(ImGui.GetWindowWidth() - (buttonSize * 3 + 20));

                if (ImGui.Button("_"))
                {
                    _window.WindowState =  WindowState.Minimized;

                }
                _isHoveringButton = ImGui.IsItemHovered();
                ImGui.SameLine();

                // Maximize/Restore button
                //var maximizeIcon = _window.WindowState == WindowState.Maximized
                //    ? _restoreIcon
                //    : _maximizeIcon;
                //if (ImGui.ImageButton((IntPtr)maximizeIcon.Handle, new Vector2(buttonSize, buttonSize)))
                if (ImGui.Button(_window.WindowState == WindowState.Maximized ? "][" : "[]"))
                {
                    _window.WindowState = _window.WindowState == WindowState.Normal
                        ? WindowState.Maximized
                        : WindowState.Normal;

                }
                _isHoveringButton = ImGui.IsItemHovered();

                ImGui.SameLine();

                // Close button
                ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                if (ImGui.Button("X"))
                //if (ImGui.ImageButton((IntPtr)_closeIcon.Handle, new Vector2(buttonSize, buttonSize)))
                {
                    _window.Close();
                }
                _isHoveringButton = ImGui.IsItemHovered();

                ImGui.PopStyleColor();

                // Resize handles
                CreateResizeHandles();

                ImGui.End();
            }
            ImGui.PopStyleVar();
        }

        private void CreateResizeHandles()
        {
            var windowSize = _window.Size;

            // Right edge
            ImGui.SetCursorScreenPos(new Vector2(windowSize.X - RESIZE_BORDER, 0));
            ImGui.InvisibleButton("resize_right", new Vector2(RESIZE_BORDER, windowSize.Y));
            if (ImGui.IsItemActive())
            {
                if (!_isResizing)
                {
                    _resizeStartMouse = GetAbsoluteMousePosition(_inputContext.Mice[0]);
                    _resizeStartSize = _window.Size;
                }
                _isResizing = true;
            }

            // Bottom edge
            ImGui.SetCursorScreenPos(new Vector2(0, windowSize.Y - RESIZE_BORDER));
            ImGui.InvisibleButton("resize_bottom", new Vector2(windowSize.X, RESIZE_BORDER));
            if (ImGui.IsItemActive())
            {
                if (!_isResizing)
                {
                    _resizeStartMouse = GetAbsoluteMousePosition(_inputContext.Mice[0]);
                    _resizeStartSize = _window.Size;
                }
                _isResizing = true;
            }

            // Bottom-right corner
            ImGui.SetCursorScreenPos(new Vector2(windowSize.X - RESIZE_BORDER, windowSize.Y - RESIZE_BORDER));
            ImGui.InvisibleButton("resize_corner", new Vector2(RESIZE_BORDER, RESIZE_BORDER));
            if (ImGui.IsItemActive())
            {
                if (!_isResizing)
                {
                    _resizeStartMouse = GetAbsoluteMousePosition(_inputContext.Mice[0]);
                    _resizeStartSize = _window.Size;
                }
                _isResizing = true;
            }
        }
        private Vector2D<int> HandleTopSnap(Rectangle<int> workArea)
        {
            if (_window.WindowState != WindowState.Maximized)
            {
                _restorePosition = _window.Position;
                _restoreSize = _window.Size;
                _window.WindowState = WindowState.Maximized;
                _wasMaximized = true;
            }
            return workArea.Center;
        }
       /* private Vector2D<int> CalculateSnappedPosition(Vector2D<float> screenMousePos)
        {
            // Gets current monitor
            var currentMonitor = _window.Monitor;

            var workArea = currentMonitor.Bounds;
            var snapThreshold = 20;

            // Top snap (maximize)
            if (screenMousePos.Y <= workArea.Position.Y + snapThreshold)
            {
                return HandleTopSnap(workArea);
            }

            // Left snap
            if (screenMousePos.X <= workArea.Position.X + snapThreshold)
            {
                return new Vector2D<int>(
                    workArea.Position.X,
                    workArea.Position.Y
                );
            }

            // Right snap
            if (screenMousePos.X >= workArea.Position.X + workArea.Size.X - snapThreshold)
            {
                return new Vector2D<int>(
                    workArea.Position.X + workArea.Size.X / 2,
                    workArea.Position.Y
                );
            }

            // Bottom restore
            if (_wasMaximized && screenMousePos.Y >= workArea.Position.Y + workArea.Size.Y - snapThreshold)
            {
                _window.WindowState = WindowState.Normal;
                _window.Position = _restorePosition;
                _window.Size = _restoreSize;
                _wasMaximized = false;
            }

            return _window.Position;
        }*/

        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
        }
        public Task OnAttach()
        {
            _window.WindowBorder = Silk.NET.Windowing.WindowBorder.Hidden;

            return Task.CompletedTask;
        }

        public void OnDetach()
        {
        }
        public void Dispose() { }

    }
}
