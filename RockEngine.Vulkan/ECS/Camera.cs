using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Utils;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Numerics;

namespace RockEngine.Vulkan.ECS
{
    internal class Camera : Component, IRenderableComponent<Camera>
    {
        public const int MAX_FOV = 120;
        public const int MIN_FOV = 30;

        private float _aspectRatio;
        private float _nearClip;
        private float _farClip;
        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projectionMatrix;
        private Matrix4x4 _viewProjectionMatrix;
        private IComponentRenderer<Camera> _renderer;

        protected Vector3 _target;
        protected float _fov = MathHelper.PiOver2;

        // Rotation around the X axis (radians)
        protected float _pitch = -MathHelper.PiOver2;
        protected float _yaw = -MathHelper.PiOver2; // Without this you would be started rotated 90 degrees right

        protected Vector3 _right = Vector3.UnitX;

        protected Vector3 _up = Vector3.UnitY;

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
                UpdateProjectionMatrix();
            }
        }

        public float NearClip
        {
            get => _nearClip;
            set
            {
                _nearClip = value;
                UpdateProjectionMatrix();
            }
        }

        public float FarClip
        {
            get => _farClip;
            set
            {
                _farClip = value;
                UpdateProjectionMatrix();
            }
        }


        public Vector3 Target
        {
            get => _target;
            set
            {
                _target = value;
                UpdateVectors();
            }
        }

        public Vector3 Up
        {
            get => _up;
            set
            {
                _up = value;
                UpdateVectors();
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
                UpdateVectors();

            }
        }
        public float Yaw
        {
            get => MathHelper.RadiansToDegrees(_yaw);
            set
            {
                _yaw = MathHelper.DegreesToRadians(value);
                UpdateVectors();
            }
        }

        public Matrix4x4 ViewProjectionMatrix => _viewProjectionMatrix;

        public IComponentRenderer<Camera> Renderer => _renderer;
     

        public override int Order => 0;

        public Camera(float fov, float aspectRatio, float nearClip, float farClip, Entity entity)
            :base(entity)
        {
            _fov = fov;
            _aspectRatio = aspectRatio;
            _nearClip = nearClip;
            _farClip = farClip;
            _target = Vector3.UnitZ;
            _up = Vector3.UnitY;
            UpdateVectors();
            UpdateProjectionMatrix();
        }

        protected void UpdateViewMatrix()
        {
            _viewMatrix = Matrix4x4.CreateLookAt(Entity.Transform.Position, _target, _up);
            UpdateViewProjectionMatrix();
        }

        protected void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, _aspectRatio, _nearClip, _farClip);
            UpdateViewProjectionMatrix();
        }

        protected void UpdateViewProjectionMatrix()
        {
            _viewProjectionMatrix = _viewMatrix * _projectionMatrix;
        }

        protected void UpdateVectors()
        {
            // First the front matrix is calculated using some basic trigonometry
            _target = new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Sin(_yaw));

            // We need to make sure the vectors are all normalized, as otherwise we would get some funky results
            _target = Vector3.Normalize(_target);

            // Calculate both the right and the up vector using cross product
            // Note that we are calculating the right from the global up, this behaviour might
            // not be what you need for all cameras so keep this in mind if you do not want a FPS camera
            _right = Vector3.Normalize(Vector3.Cross(_target, Vector3.UnitY));
            _up = Vector3.Normalize(Vector3.Cross(_right, _target));
            UpdateViewMatrix();
        }

        public override async Task OnInitializedAsync(VulkanContext context)
        {
            try
            {
                _renderer = IoC.Container.GetRenderer<Camera>();
                await _renderer.InitializeAsync(this, context)
                    .ConfigureAwait(false);
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Initialization failed: {ex.Message}");
                throw;
            }
        }

        public Task RenderAsync(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            return _renderer.RenderAsync(this, context, commandBuffer);
        }
    }
}