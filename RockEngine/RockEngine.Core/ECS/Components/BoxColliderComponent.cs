using System.Numerics;

using JoltPhysicsSharp;

namespace RockEngine.Core.ECS.Components.Physics
{
    public partial class BoxColliderComponent : ColliderComponent
    {
        private Vector3 _extents = new Vector3(0.5f, 0.5f, 0.5f);
        private Quaternion _rotation = Quaternion.Identity;

        public Vector3 Extents
        {
            get => _extents;
            set
            {
                if (_extents != value)
                {
                    _extents = value;
                    NotifyChanged();
                }
            }
        }

        public Quaternion Rotation
        {
            get => _rotation;
            set
            {
                if (_rotation != value)
                {
                    _rotation = value;
                    NotifyChanged();
                }
            }
        }

        public Vector3 Size
        {
            get => _extents * 2;
            set => Extents = value * 0.5f;
        }

        public override Shape CreateShape()
        {
            return new BoxShape(_extents);
        }
    }
}
