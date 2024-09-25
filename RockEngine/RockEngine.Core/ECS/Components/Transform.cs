using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace RockEngine.Core.ECS.Components
{
    public class Transform : IComponent
    {
        private UniformBuffer _buffer;

        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
        public Transform()
        {
        }

        public Matrix4x4 GetModelMatrix()
        { 
            // Create scale, rotation, and translation matrices
            var scaleMatrix = Matrix4x4.CreateScale(Scale);
            var rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
            var translationMatrix = Matrix4x4.CreateTranslation(Position);
            return scaleMatrix * rotationMatrix * translationMatrix;
        }

        public async ValueTask Init(RenderingContext context, Renderer renderer)
        {
            _buffer = new UniformBuffer(context, (ulong)Unsafe.SizeOf<Matrix4x4>());
        }

        public ValueTask Render(Renderer renderer)
        {
            return default;
        }

        public void Update()
        {
            
        }
    }
}
