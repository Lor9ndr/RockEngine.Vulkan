﻿using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.RenderTargets;

using Silk.NET.Vulkan;

using System.Numerics;

namespace RockEngine.Core.ECS.Components
{
    public class Camera : Component
    {
        public const int MAX_FOV = 120;
        public const int MIN_FOV = 30;
        private float _aspectRatio;
        private float _nearClip;
        private float _farClip;
        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projectionMatrix;
        private Matrix4x4 _viewProjectionMatrix;
        private float _fov = MathHelper.PiOver2;

        // Rotation around the X axis (radians)
        private float _pitch = -MathHelper.PiOver2;
        private float _yaw = -MathHelper.PiOver2; // Without this you would be started rotated 90 degrees right

        private Vector3 _right = Vector3.UnitX;

        private Vector3 _up;
        private Vector3 _front;

        public Vector3 Right => _right;

        public Vector3 Up => _up;
        public Matrix4x4 ViewProjectionMatrix => _viewProjectionMatrix;

        public float Fov
        {
            get => MathHelper.RadiansToDegrees(_fov);
            set
            {
                var angle = Math.Clamp(value, MIN_FOV, MAX_FOV);
                _fov = MathHelper.DegreesToRadians(angle);
            }
        }

        public float AspectRatio
        {
            get => _aspectRatio;
            set
            {
                _aspectRatio = value;
            }
        }

        public float NearClip
        {
            get => _nearClip;
            set
            {
                _nearClip = value;
            }
        }

        public float FarClip
        {
            get => _farClip;
            set
            {
                _farClip = value;
            }
        }

        public Vector3 Front
        {
            get => _front;
            set
            {
                _front = value;
            }
        }

        public float Pitch
        {
            get => MathHelper.RadiansToDegrees(_pitch);
            set
            {
                // We clamp the pitch value between -89 and 89 to prevent the camera from going upside down, and a bunch
                // of weird "bugs" when you are using euler angles for rotation.
                // If you want to read more about this you can try researching a topic called gimbal lock
                var angle = Math.Clamp(value, -89f, 89f);
                _pitch = MathHelper.DegreesToRadians(angle);
            }
        }

        public float Yaw
        {
            get => MathHelper.RadiansToDegrees(_yaw);
            set
            {
                _yaw = MathHelper.DegreesToRadians(value);
            }
        }


        public CameraRenderTarget RenderTarget { get; set; }


        public Camera()
        {
            _fov = MathHelper.DegreesToRadians(90);
            _aspectRatio = 16 / 9; // just for now, we have to change it by window
            _nearClip = 0.1f;
            _farClip = 1000;
        }
        public void UpdateViewMatrix()
        {
            _viewMatrix = Matrix4x4.CreateLookAt(Entity.Transform.WorldPosition, Entity.Transform.WorldPosition + Front, _up);
            UpdateProjectionMatrix();
        }

        public void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, _aspectRatio, _nearClip, _farClip);
            // flipside the perspective because vulkan(or System.Numerics) idk
            _projectionMatrix.M22 *= -1;

            UpdateViewProjectionMatrix();
        }

        public void UpdateViewProjectionMatrix()
        {
            _viewProjectionMatrix = _viewMatrix * _projectionMatrix;
        }

        public void UpdateVectors()
        {
            // First the front matrix is calculated using some basic trigonometry
            _front = new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Sin(_yaw));

            // We need to make sure the vectors are all normalized, as otherwise we would get some funky results
            _front = Vector3.Normalize(_front);

            // Calculate both the right and the up vector using cross product
            // Note that we are calculating the right from the global up, this behaviour might
            // not be what you need for all cameras so keep this in mind if you do not want a FPS camera
            _right = Vector3.Normalize(Vector3.Cross(_front, Vector3.UnitY));
            _up = Vector3.Normalize(Vector3.Cross(_right, _front));
            UpdateViewMatrix();
        }

        public override ValueTask OnStart(Renderer renderer)
        {
            renderer.RegisterCamera(this);
            return default;
        }


        public override ValueTask Update(Renderer renderer)
        {
            UpdateVectors();
            return default;
        }

    }
}
