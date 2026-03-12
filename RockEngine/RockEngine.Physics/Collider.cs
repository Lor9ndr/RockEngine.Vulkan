using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;

using System.Numerics;

namespace RockEngine.Physics
{
    public partial class Collider : Component
    {
        public ColliderShape Shape { get; set; } = ColliderShape.Box;

        [Step(0.1f)]
        public Vector3 Size { get; set; } = Vector3.One;

        [Step(0.1f)]
        public float Radius { get; set; } = 0.5f;

        [Step(0.1f)]
        public float Height { get; set; } = 2.0f;

        public Vector3 Offset { get; set; } = Vector3.Zero;

        public bool IsTrigger { get; set; } = false;

        [SerializeIgnore]
        public Matrix4x4 WorldBounds
        {
            get
            {
                var transform = Entity.Transform;
                var scale = transform.WorldScale;
                var translation = transform.WorldPosition;

                var sizeMatrix = Matrix4x4.CreateScale(Size * scale);
                var offsetMatrix = Matrix4x4.CreateTranslation(Offset + translation);

                return sizeMatrix * offsetMatrix;
            }
        }

        public override ValueTask OnStart(WorldRenderer renderer)
        {
            var physicsManager = renderer.PhysicsManager;
            physicsManager?.RegisterCollider(this);
            return default;
        }

        public override void Destroy()
        {
            var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
            physicsManager?.UnregisterCollider(this);
            base.Destroy();
        }
    }
}