using ImGuiNET;

using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Editor.EditorComponents;
using RockEngine.Editor.EditorUI.ImGuiRendering;

using Silk.NET.Input;
using Silk.NET.Vulkan;

using System.Numerics;

using ZLinq;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class ViewportWindow : EditorWindow
    {
        private readonly World _world;
        private readonly IInputContext _inputContext;
        private readonly ImGuiController _imGuiController;
        private Vector2 _currentSize;
        private readonly bool _isSceneView;

        public ViewportWindow(string title, World world, IInputContext inputContext, ImGuiController imGuiController)
            : base(title)
        {
            _world = world;
            _inputContext = inputContext;
            _imGuiController = imGuiController;
            _isSceneView = title == "Scene View";
        }

        public override void Draw()
        {
            if (!IsOpen) return;

            var camera = GetCamera();
            if (ImGui.Begin(Title, ref _isOpen))
            {
                camera?.Entity.SetActive(true);

                OnDraw();
            }
            else
            {
                camera?.Entity.SetActive(false);

            }
            ImGui.End();
        }
        protected override void OnDraw()
        {
            var camera = GetCamera();
            if (camera != null)
            {
                if (_isSceneView && camera is DebugCamera debugCam)
                {
                    HandleViewportInteraction(debugCam);
                }

                DrawRenderTarget(camera.RenderTarget, ref _currentSize);
            }

            DrawViewportOverlay(_isSceneView ? "SCENE VIEW" : "GAME VIEW");
        }

        public void Update()
        {
            var camera = GetCamera();
            if (camera != null)
            {

                _currentSize.X = Math.Max(_currentSize.X, 1);
                _currentSize.Y = Math.Max(_currentSize.Y, 1);
                camera.RenderTarget?.Resize(new Extent2D((uint)_currentSize.X, (uint)_currentSize.Y));
            }
        }

        private Camera GetCamera()
        {
            if (_isSceneView)
            {
                return _world.GetEntities()
                    .FirstOrDefault(s => s.GetComponent<DebugCamera>() is not null)?
                    .GetComponent<DebugCamera>();
            }
            else
            {
                return _world.GetEntities()
                    .FirstOrDefault(s => s.GetComponent<Camera>() is not null && s.GetComponent<DebugCamera>() is null)?
                    .GetComponent<Camera>();
            }
        }

        private void HandleViewportInteraction(DebugCamera debugCam)
        {
            var windowHovered = ImGui.IsWindowHovered();

            if (windowHovered)
            {
                _inputContext.Mice[0].Cursor.StandardCursor = StandardCursor.Hand;
            }
            else
            {
                _inputContext.Mice[0].Cursor.StandardCursor = StandardCursor.Arrow;
            }

            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                debugCam.CanMove = windowHovered;
            }
            else
            {
                debugCam.CanMove = false;
            }
        }

        private void DrawRenderTarget(RenderTarget renderTarget, ref Vector2 currentSize)
        {
            if (renderTarget != null)
            {
                var texId = _imGuiController.GetTextureID(renderTarget.OutputTexture);
                var imageSize = new Vector2(renderTarget.OutputTexture.Width, renderTarget.OutputTexture.Height);
                var availableSize = ImGui.GetContentRegionAvail();
                var scale = Math.Min(availableSize.X / imageSize.X, availableSize.Y / imageSize.Y);
                var displaySize = imageSize * scale;

                // Center the image
                var cursorPos = ImGui.GetCursorPos();
                var windowSize = ImGui.GetWindowSize();
                var imagePos = new Vector2(
                    (windowSize.X - displaySize.X) * 0.5f,
                    (windowSize.Y - displaySize.Y) * 0.5f
                );

                ImGui.SetCursorPos(imagePos);
                ImGui.Image(texId, displaySize);

                currentSize = availableSize;
            }
        }

        private void DrawViewportOverlay(string title)
        {
            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();

            // Draw semi-transparent title bar
            var titleBarHeight = 24;
            var titleBarRect = new Vector4(
                windowPos.X, windowPos.Y,
                windowPos.X + windowSize.X, windowPos.Y + titleBarHeight
            );

            drawList.AddRectFilled(
                new Vector2(titleBarRect.X, titleBarRect.Y),
                new Vector2(titleBarRect.Z, titleBarRect.W),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.8f))
            );

            // Draw title
            var textPos = new Vector2(windowPos.X + 8, windowPos.Y + 4);
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), title);
        }
    }
}