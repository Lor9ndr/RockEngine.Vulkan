/*using RockEngine.Core;
using RockEngine.Core.ECS;

using Silk.NET.Input;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RockEngine.Editor.EditorComponents.Systems
{
    public class DebugCameraSystem : ISystem
    {
        private readonly IInputContext _inputContext;
        private float _movementSpeed;

        public int Priority => 300;

        public DebugCameraSystem(IInputContext inputContext)
        {
            _inputContext = inputContext;
            foreach (var mouse in _inputContext.Mice)
            {
                mouse.MouseMove += OnMouseMove;
            }
        }

        public ValueTask Update(World world, float deltaTime)
        {
            var debugCamEntity = world.GetComponentsWithEntities<DebugCamera>().FirstOrDefault();
            if(debugCamEntity is not null)
            {
                _movementSpeed += _inputContext.Mice[0].ScrollWheels[0].Y;
                HandleKeyboardInput(debugCamEntity.Entity);
            }
        }
        private void HandleKeyboardInput(in Entity entity)
        {
            if (_inputContext == null) return;

            foreach (var keyboard in _inputContext.Keyboards)
            {
                var position = entity.Transform.Position;

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

                entity.Transform.Position = position;
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
*/