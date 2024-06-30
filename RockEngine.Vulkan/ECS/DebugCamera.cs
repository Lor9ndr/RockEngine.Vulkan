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

            foreach (var mouse in _inputContext.Mice)
            {
                mouse.MouseMove += OnMouseMove;
                //mouse.Cursor.CursorMode = CursorMode.Raw;
            }
        }
        private void ChangePosition(Vector3 offset)
        {
             Entity!.Transform.Position += offset;
        }
        public override void Update(double time)
        {
            float t = (float)time;
            var keyboard = _inputContext.Keyboards[0];

            if (keyboard.IsKeyPressed(Key.A))
            {
                ChangePosition(-Right * _moveSpeed * t);
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.S))
            {
                ChangePosition(-Front * _moveSpeed * t);
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.W))
            {
                ChangePosition(Front * _moveSpeed * t); 
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.D))
            {
                ChangePosition(Right * _moveSpeed * t);
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.Space))
            {
                ChangePosition(Up * _moveSpeed * t);
                UpdateVectors();
            }
            if (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight))
            {
                ChangePosition(-Up * _moveSpeed * t);
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
            Pitch -= deltaY; 

            // Update the last mouse position
            _lastMousePosition = position;
        }
    }
}