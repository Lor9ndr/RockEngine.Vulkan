using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.ResourceBindings;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RockEngine.Core.ECS.Components
{
    public class Transform : Component
    {
        private UniformBuffer? _buffer;

        public Vector3 Position = new Vector3(0);
        public Quaternion Rotation = new Quaternion(0, 0, 0, 1);
        public Vector3 Scale = Vector3.One;
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

        public override ValueTask OnStart(Renderer renderer)
        {
            var mesh = Entity.GetComponent<Mesh>();
            if (mesh is not null)
            {
                _buffer = new UniformBuffer("ModelData", 0, (ulong)Unsafe.SizeOf<Matrix4x4>());
                //renderer.RegisterBuffer(_buffer, 1);
                mesh.Material.AddBinding(new UniformBufferBinding(_buffer, 0, 1));
            }
            
            return default;
        }
       
        public override ValueTask Update(Renderer renderer)
        {
            if (_buffer is null)
            {
                return default;
            }
            //renderer.BindUniformBuffer(_buffer, 1);
            return _buffer.UpdateAsync(GetModelMatrix());
        }
    }
}
