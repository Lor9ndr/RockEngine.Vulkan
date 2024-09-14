using System.Numerics;

namespace RockEngine.Core.ECS.Components
{
    public struct Transform
    {
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

        public readonly Matrix4x4 GetModelMatrix()
        { 
            // Create scale, rotation, and translation matrices
            var scaleMatrix = Matrix4x4.CreateScale(Scale);
            var rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
            var translationMatrix = Matrix4x4.CreateTranslation(Position);
            return scaleMatrix * rotationMatrix * translationMatrix;
        }
    }
}
