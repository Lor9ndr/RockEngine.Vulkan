using RockEngine.Core;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;

using Silk.NET.Input;

using System.Numerics;

namespace RockEngine.Editor.EditorComponents
{
    internal class DebugCamera : Camera
    {
        private IInputContext _inputContext;
        private float _movementSpeed = 5.0f; // Speed of movement
        private readonly float _mouseSensitivity = 0.1f; // Sensitivity for mouse movement
        private Vector2 _lastMousePosition;
        private bool _firstMouse = true;

        public DebugCamera()
        {
        }

        public void SetInputContext(IInputContext inputContext)
        {
            _inputContext = inputContext;

            // Ensure the mouse is captured and events are handled
            foreach (var mouse in _inputContext.Mice)
            {
                mouse.MouseMove += OnMouseMove;
            }
        }

        public override ValueTask Update(Renderer renderer)
        {
            _movementSpeed += _inputContext.Mice[0].ScrollWheels[0].Y;
            HandleKeyboardInput();
            return base.Update(renderer);
        }

        private void HandleKeyboardInput()
        {
            if (_inputContext == null) return;

            foreach (var keyboard in _inputContext.Keyboards)
            {
                var position = Entity.Transform.Position;

                // Move forward
                if (keyboard.IsKeyPressed(Key.W))
                {
                    position += Front * _movementSpeed * Time.DeltaTime;
                }

                // Move backward
                if (keyboard.IsKeyPressed(Key.S))
                {
                    position -= Front * _movementSpeed * Time.DeltaTime;
                }

                // Move right
                if (keyboard.IsKeyPressed(Key.D))
                {
                    position += Right * _movementSpeed * Time.DeltaTime;
                }

                // Move left
                if (keyboard.IsKeyPressed(Key.A))
                {
                    position -= Right * _movementSpeed * Time.DeltaTime;
                }

                // Move up
                if (keyboard.IsKeyPressed(Key.Space))
                {
                    position += Up * _movementSpeed * Time.DeltaTime;
                }

                // Move down
                if (keyboard.IsKeyPressed(Key.ShiftLeft))
                {
                    position -= Up * _movementSpeed * Time.DeltaTime;
                }

                Entity.Transform.Position = position;
            }
        }

        private void OnMouseMove(IMouse mouse, Vector2 position)
        {
            if (_firstMouse)
            {
                _lastMousePosition = position;
                _firstMouse = false;
            }

            var xOffset = (position.X - _lastMousePosition.X) * _mouseSensitivity;
            var yOffset = (position.Y - _lastMousePosition.Y) * _mouseSensitivity;

            _lastMousePosition = position;

            Yaw += xOffset;
            Pitch -= yOffset; // Invert Y-axis for natural camera movement

            UpdateVectors();
        }
    }
}
