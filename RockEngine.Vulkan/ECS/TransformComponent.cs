using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.ComponentRenderers;

using System.Numerics;

namespace RockEngine.Vulkan.ECS
{
    public class TransformComponent : Component, IRenderableComponent<TransformComponent>
    {
        private Vector3 _position;
        private Quaternion _rotation = Quaternion.Identity;
        private Vector3 _scale = Vector3.One;
        private Matrix4x4 _modelMatrix;
        private bool _isDirty = true;
        private IComponentRenderer<TransformComponent> _renderer;

        public TransformComponent(IComponentRenderer<TransformComponent> renderer)
        {
            _renderer = renderer;
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

        public IComponentRenderer<TransformComponent> Renderer => _renderer;

        public int Order => 0;

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

        public override async Task OnInitializedAsync()
        {
            try
            {
                _renderer = IoC.Container.GetRenderer<TransformComponent>();
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