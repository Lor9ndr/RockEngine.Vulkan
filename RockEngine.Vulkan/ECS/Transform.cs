using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Numerics;

namespace RockEngine.Vulkan.ECS
{
    public class Transform : Component, IRenderableComponent<Transform>
    {
        private Vector3 _position;
        private Quaternion _rotation = Quaternion.Identity;
        private Vector3 _scale = Vector3.One;
        private Matrix4x4 _modelMatrix;
        private bool _isDirty = true;
        private IComponentRenderer<Transform> _renderer;

        public Transform(Entity entity) : base(entity)
        {
        }

        public Vector3 Position
        {
            get => _position;
            set
            {
                _position = value;
                _isDirty = true;
            }
        }

        public Quaternion Rotation
        {
            get => _rotation;
            set
            {
                _rotation = value;
                _isDirty = true;
            }
        }

        public Vector3 Scale
        {
            get => _scale;
            set
            {
                _scale = value;
                _isDirty = true;
            }
        }

        public IComponentRenderer<Transform> Renderer => _renderer;

        public override int Order => 0;

        public ref Matrix4x4 GetModelMatrix()
        {
            if (_isDirty)
            {
                // Create scale, rotation, and translation matrices
                var scaleMatrix = Matrix4x4.CreateScale(_scale);
                var rotationMatrix = Matrix4x4.CreateFromQuaternion(_rotation);
                var translationMatrix = Matrix4x4.CreateTranslation(_position);

                // Combine the matrices to get the model matrix
                _modelMatrix = scaleMatrix * rotationMatrix * translationMatrix;
                _isDirty = false;
            }

            return ref _modelMatrix;
        }

        public override async Task OnInitializedAsync(VulkanContext context)
        {
            try
            {
                _renderer = IoC.Container.GetRenderer<Transform>();
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