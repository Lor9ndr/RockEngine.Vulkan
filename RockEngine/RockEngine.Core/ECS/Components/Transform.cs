using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.ResourceBindings;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.ECS.Components
{
    public class Transform : Component
    {
        private static UniformBuffer? _buffer;
        private static ulong _offsetCounter = 0; // Counter for offset allocation
        private readonly ulong _offset; // Store the offset for this instance

        public Vector3 Position = new Vector3(0);
        public Quaternion Rotation = new Quaternion(0, 0, 0, 1);
        public Vector3 Scale = Vector3.One;

        private readonly int _dataSize = Marshal.SizeOf<Matrix4x4>();

        public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            _offset = _offsetCounter; // Assign the next available offset
            _offsetCounter += (ulong)Unsafe.SizeOf<Matrix4x4>(); // Increment for the next instance
        }

        public Transform() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

        public Matrix4x4 GetModelMatrix()
        {
            var scaleMatrix = Matrix4x4.CreateScale(Scale);
            var rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
            var translationMatrix = Matrix4x4.CreateTranslation(Position);
            return scaleMatrix * rotationMatrix * translationMatrix;
        }

        public override ValueTask OnStart(Renderer renderer)
        {
            var mesh = Entity.GetComponent<Mesh>();
            // Initialize only once
            if (mesh is not null && _buffer == null) 
            {
                _buffer = new UniformBuffer("ModelData", 0, (ulong)(1024 * 1024 * _dataSize), _dataSize, true); 
            }
            mesh?.Material.Bindings.Add(new UniformBufferBinding(_buffer!, 0, 1, _offset));

            return default;
        }

        public override ValueTask Update(Renderer renderer)
        {
            if (_buffer is null)
            {
                return default;
            }
            // Use the calculated offset when updating
            return _buffer.UpdateAsync(GetModelMatrix(), (ulong)_dataSize, _offset);
        }
    }
}
