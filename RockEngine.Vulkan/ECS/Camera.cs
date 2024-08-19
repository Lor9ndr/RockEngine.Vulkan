using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Utils;
using RockEngine.Vulkan.VkObjects;

using System.Numerics;

namespace RockEngine.Vulkan.ECS
{
    public class Camera : Component, IRenderableComponent<Camera>
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

        protected float _fov = MathHelper.PiOver2;

        // Rotation around the X axis (radians)
        protected float _pitch = -MathHelper.PiOver2;
        protected float _yaw = -MathHelper.PiOver2; // Without this you would be started rotated 90 degrees right

        protected Vector3 _right = Vector3.UnitX;

        protected Vector3 _up ;
        private Vector3 _front;

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


        public Vector3 Right => _right;

        public Vector3 Up => _up;

        public Vector3 Front
        {
            get => _front;
            set
            {
                _front = value;
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
     

        public int Order => 0;

        public Camera(IComponentRenderer<Camera> renderer)
        {
            _renderer = renderer;
            _fov = MathHelper.DegreesToRadians(90);
            _aspectRatio = 16/9; // just for now, we have to change it by window
            _nearClip = 0.1f;
            _farClip = 1000;

        }

        protected void UpdateViewMatrix()
        {
            _viewMatrix = Matrix4x4.CreateLookAt(Entity.Transform.Position, Entity.Transform.Position + Front, _up);
            UpdateViewProjectionMatrix();
        }

        protected void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, _aspectRatio, _nearClip, _farClip);
            // flipside the perspective because vulkan(or System.Numerics) idk
            _projectionMatrix[1,1] *= -1;
            UpdateViewProjectionMatrix();
        }

        protected void UpdateViewProjectionMatrix()
        {
            _viewProjectionMatrix = _viewMatrix * _projectionMatrix;
        }

        protected void UpdateVectors()
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

        public override async Task OnInitializedAsync()
        {
            UpdateVectors();
            UpdateProjectionMatrix();
            try
            {
                await _renderer.InitializeAsync(this)
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

        public ValueTask RenderAsync(FrameInfo frameInfo)
        {
            return _renderer.RenderAsync(this, frameInfo);
        }
    }
}