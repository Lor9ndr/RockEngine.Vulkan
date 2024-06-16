using RockEngine.Vulkan.Utils;

using Silk.NET.Input;

using System.Numerics;

namespace RockEngine.Vulkan.ECS
{
    internal class DebugCamera : Camera
    {
        private readonly IInputContext _inputContext;
        private float _moveSpeed = 0.5f;
        private float _rotationSpeed = 0.1f;
        private Vector2 _lastMousePosition;
        private bool _firstMouseMove = true;

        public DebugCamera(IInputContext context, float fov, float aspectRatio, float nearClip, float farClip, Entity entity)
            : base(fov, aspectRatio, nearClip, farClip, entity)
        {
            _inputContext = context;
            _target = Vector3.Normalize(Target);
            _right = Vector3.Normalize(Vector3.Cross(_target, Up));

            foreach (var mouse in _inputContext.Mice)
            {
                mouse.MouseMove += OnMouseMove;
            }
        }

        public override void Update(double time)
        {
            float t = (float)time;
            var keyboard = _inputContext.Keyboards[0];

            if (keyboard.IsKeyPressed(Key.A))
            {
                Entity.Transform.Position -= _right * _moveSpeed * t;
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.S))
            {
                Entity.Transform.Position -= _target * _moveSpeed * t;
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.W))
            {
                Entity.Transform.Position += _target * _moveSpeed * t;
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.D))
            {
                Entity.Transform.Position += _right * _moveSpeed * t;
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.Space))
            {
                Entity.Transform.Position += Vector3.UnitY * _moveSpeed * t;
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight))
            {
                Entity.Transform.Position -= Vector3.UnitY * _moveSpeed * t;
                UpdateVectors();
            }
        }

        private void OnMouseMove(IMouse mouse, Vector2 position)
        {
            if (_firstMouseMove)
            {
                _lastMousePosition = position;
                _firstMouseMove = false;
            }

            // Calculate the change in mouse position
            var deltaX = (position.X - _lastMousePosition.X) * _rotationSpeed;
            var deltaY = (position.Y - _lastMousePosition.Y) * _rotationSpeed;

            // Update the yaw and pitch
            Yaw += deltaX;
            Pitch -= deltaY; // Subtracting because moving the mouse up should decrease the pitch

            // Clamp the pitch to prevent flipping
            Pitch = Math.Clamp(Pitch, -89.0f, 89.0f);

            // Update the last mouse position
            _lastMousePosition = position;
        }
    }
}