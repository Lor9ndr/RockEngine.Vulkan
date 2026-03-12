using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components.RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Physics
{
    public enum PhysicsBodyType
    {
        Static,
        Dynamic,
        Kinematic
    }

    public enum ColliderShape
    {
        Sphere,
        Box,
        Capsule,
        Cylinder,
        Mesh,
    }


    public partial class Rigidbody : Component
    {
        private Vector3 _velocity;
        private Vector3 _angularVelocity;
        private Vector3 _forceAccumulator;
        private Vector3 _torqueAccumulator;

        [Step(0.1f), Range(0.0f, 1000.0f)]
        public float Mass { get; set; } = 1.0f;

        [Step(0.1f), Range(0.0f, 1.0f)]
        public float Restitution { get; set; } = 0.5f;

        [Step(0.1f), Range(0.0f, 1.0f)]
        public float Friction { get; set; } = 0.7f;

        [Step(0.1f), Range(0.0f, 10.0f)]
        public float LinearDamping { get; set; } = 0.01f;

        [Step(0.1f), Range(0.0f, 10.0f)]
        public float AngularDamping { get; set; } = 0.01f;

        public PhysicsBodyType BodyType { get; set; } = PhysicsBodyType.Dynamic;
        public bool UseGravity { get; set; } = true;

        [SerializeIgnore]
        public Vector3 Velocity
        {
            get => _velocity;
            set => _velocity = value;
        }

        [SerializeIgnore]
        public Vector3 AngularVelocity
        {
            get => _angularVelocity;
            set => _angularVelocity = value;
        }

        public RigidBodyState State { get;set; } = RigidBodyState.Active;

        [SerializeIgnore]
        public IReadOnlyList<Constraint> ConnectedConstraints => _connectedConstraints;

        public Vector3 Force { get; set; }
        public Vector3 Torque { get; set; }

        private readonly List<Constraint> _connectedConstraints = new();
        public void AddForce(Vector3 force, Vector3? point = null)
        {
            if (BodyType != PhysicsBodyType.Dynamic) return;

            _forceAccumulator += force;

            if (point.HasValue)
            {
                Vector3 r = point.Value - Entity.Transform.WorldPosition;
                _torqueAccumulator += Vector3.Cross(r, force);
            }
        }

        public void AddImpulse(Vector3 impulse, Vector3? point = null)
        {
            if (BodyType != PhysicsBodyType.Dynamic) return;

            _velocity += impulse / Mass;

            if (point.HasValue)
            {
                // TODO: Add angular impulse calculation
            }
        }

        public void ClearForces()
        {
            _forceAccumulator = Vector3.Zero;
            _torqueAccumulator = Vector3.Zero;
        }

        internal void Integrate(float deltaTime, Vector3 gravity)
        {
            if (BodyType != PhysicsBodyType.Dynamic) return;

            // Apply gravity
            if (UseGravity)
            {
                _forceAccumulator += gravity * Mass;
            }

            // Calculate acceleration
            Vector3 acceleration = _forceAccumulator / Mass;

            // Update velocity
            _velocity += acceleration * deltaTime;
            _velocity *= MathF.Pow(1.0f - LinearDamping, deltaTime);

            // Update position
            Entity.Transform.Position += _velocity * deltaTime;

            // TODO: Update rotation from angular velocity

            // Clear forces
            ClearForces();
        }
        internal void ConstraintAdded(Constraint constraint)
        {
            if (!_connectedConstraints.Contains(constraint))
            {
                _connectedConstraints.Add(constraint);
            }
        }

        internal void ConstraintRemoved(Constraint constraint)
        {
            _connectedConstraints.Remove(constraint);
        }

        public bool HasConstraint(Constraint constraint)
        {
            return _connectedConstraints.Contains(constraint);
        }

        public bool HasConstraintsOfType(ConstraintType type)
        {
            foreach (var constraint in _connectedConstraints)
            {
                if (constraint.Type == type)
                    return true;
            }
            return false;
        }

        public void RemoveAllConstraints()
        {
            foreach (var constraint in _connectedConstraints.ToArray())
            {
                constraint.Destroy();
            }
            _connectedConstraints.Clear();
        }

        public override ValueTask OnStart(WorldRenderer renderer)
        {
            var physicsManager = renderer.PhysicsManager;
            physicsManager?.RegisterRigidbody(this);
            return default;
        }

        public override ValueTask Update(WorldRenderer renderer)
        {
            // Physics integration is handled by PhysicsManager
            return default;
        }

        public override void Destroy()
        {
            var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
            physicsManager?.UnregisterRigidbody(this);
            base.Destroy();
        }
    }
}