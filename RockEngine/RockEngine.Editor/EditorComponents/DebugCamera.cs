using RockEngine.Core;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;

using Silk.NET.Input;
using Silk.NET.Input.Extensions;

using System.Numerics;

namespace RockEngine.Editor.EditorComponents
{
    internal partial class DebugCamera : Camera
    {
        private readonly InputManager _inputManager;
        private float _movementSpeed = 5.0f; // Speed of movement
        private readonly float _mouseSensitivity = 0.1f; // Sensitivity for mouse movement
        private Vector2 _lastMousePosition;
        private bool _firstMouse = true;
        public bool CanMove = false;
        public override RenderLayerMask VisibleLayers { get;set; } = RenderLayerMask.All;

        public DebugCamera(InputManager inputManager)
        {
            _inputManager = inputManager;

            // Ensure the mouse is captured and events are handled

            foreach (var mouse in _inputManager.Context.Mice)
            {
                mouse.MouseMove += OnMouseMove;
            }
            _inputManager.OnInputActionChanged += (oldContext, newContext)=>
            {
                foreach (var mouse in oldContext.Mice)
                {
                    mouse.MouseMove -= OnMouseMove;
                }
                foreach (var mouse in newContext.Mice)
                {
                    mouse.MouseMove += OnMouseMove;
                }

            };
        }

    
        public override ValueTask Update(WorldRenderer renderer)
        {
            HandleKeyboardInput();
            return base.Update(renderer);
        }

        private void HandleKeyboardInput()
        {
            if (_inputManager == null || !CanMove)
            {
                return;
            }
            _movementSpeed += _inputManager.PrimaryMouse.CaptureState().GetScrollWheels()[0].Y;

            var keyboard = _inputManager.PrimaryKeyboard;
            {
                var position = Entity.Transform.WorldPosition;

                // Move forward
                if (keyboard.IsKeyPressed(Key.W))
                {
                    position += Forward * _movementSpeed * Time.DeltaTime;
                }

                // Move backward
                if (keyboard.IsKeyPressed(Key.S))
                {
                    position -= Forward * _movementSpeed * Time.DeltaTime;
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

            if (!CanMove)
            {
                return;
            }
            Yaw += xOffset;
            Pitch -= yOffset; // Invert Y-axis for natural camera movement

            UpdateVectors();
        }

        public override ValueTask OnStart(WorldRenderer renderer)
        {
            Entity.AddComponent<InfinityGrid>();
            World.GetCurrent().CreateEntity("GIZMO").AddComponent<TransformGizmo>();
            return base.OnStart(renderer);
        }
    }
}
