using System.Numerics;

using JoltPhysicsSharp;

using MessagePack;

namespace RockEngine.Core.ECS.Components.Physics
{
    [MessagePackObject(AllowPrivate = true)]
    public partial class BoxColliderComponent : ColliderComponent
    {
        [IgnoreMember]
        private Vector3 _extents = new Vector3(0.5f, 0.5f, 0.5f);
        [IgnoreMember]
        private Quaternion _rotation = Quaternion.Identity;

        [Key(0)]
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

        [Key(1)]
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

        [Key(2)]
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
