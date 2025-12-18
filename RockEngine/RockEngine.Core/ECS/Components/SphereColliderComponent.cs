using System.Numerics;

using JoltPhysicsSharp;

namespace RockEngine.Core.ECS.Components
{
    public partial class SphereColliderComponent : ColliderComponent
    {
        private float _radius = 0.5f;

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

        public float Diameter
        {
            get => _radius * 2;
            set => Radius = value * 0.5f;
        }

        public override Shape CreateShape()
        {
            return new SphereShape(_radius);
        }
    }
}