using System.Numerics;

using JoltPhysicsSharp;

using MessagePack;

namespace RockEngine.Core.ECS.Components
{
    public enum CapsuleOrientation
    {
        YAxis,  // Up/Down (default for characters)
        XAxis,  // Left/Right
        ZAxis   // Forward/Back
    }

    [MessagePackObject(AllowPrivate = true)]
    public partial class CapsuleColliderComponent : ColliderComponent
    {
        [IgnoreMember]
        private float _height = 1.0f;
        [IgnoreMember]

        private float _radius = 0.5f;
        [IgnoreMember]

        private CapsuleOrientation _orientation = CapsuleOrientation.YAxis;

        [Key(10)]
        public float Height
        {
            get => _height;
            set
            {
                if (Math.Abs(_height - value) > 0.0001f)
                {
                    _height = Math.Max(0.001f, value);
                    NotifyChanged();
                }
            }
        }

        [Key(11)]
        public float Radius
        {
            get => _radius;
            set
            {
                if (Math.Abs(_radius - value) > 0.0001f)
                {
                    _radius = Math.Max(0.001f, value);
                    NotifyChanged();
                }
            }
        }

        [Key(12)]
        public CapsuleOrientation Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation != value)
                {
                    _orientation = value;
                    NotifyChanged();
                }
            }
        }

        [IgnoreMember]

        public float HalfHeight => Height * 0.5f;

        public override Shape CreateShape()
        {
            return new CapsuleShape(HalfHeight, _radius);
        }
    }
}