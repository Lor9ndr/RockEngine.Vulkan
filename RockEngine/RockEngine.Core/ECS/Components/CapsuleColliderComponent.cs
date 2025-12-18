using System.Numerics;

using JoltPhysicsSharp;

namespace RockEngine.Core.ECS.Components
{
    public enum CapsuleOrientation
    {
        YAxis,  // Up/Down (default for characters)
        XAxis,  // Left/Right
        ZAxis   // Forward/Back
    }

    public partial class CapsuleColliderComponent : ColliderComponent
    {
        private float _height = 1.0f;
        private float _radius = 0.5f;
        private CapsuleOrientation _orientation = CapsuleOrientation.YAxis;

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

        public float HalfHeight => Height * 0.5f;

        public override Shape CreateShape()
        {
            return new CapsuleShape(HalfHeight, _radius);
        }
    }
}