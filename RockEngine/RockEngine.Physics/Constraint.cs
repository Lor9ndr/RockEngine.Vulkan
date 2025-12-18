using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Physics;

using System.Data;
using System.Numerics;

using static RockEngine.Core.Rendering.Managers.PhysicsManager;

namespace RockEngine.Core.ECS.Components
{

namespace RockEngine.Core.ECS.Components
    {
        public partial class Constraint : Component
        {
            private Rigidbody _connectedBodyA;
            private Rigidbody _connectedBodyB;
            private float _accumulatedImpulse;
            private float _accumulatedError;
            private Vector3 _lastAnchorA;
            private Vector3 _lastAnchorB;
            private bool _isBroken;
            private float _breakTimer;

            #region Properties

            [SerializeIgnore]
            public Rigidbody BodyA
            {
                get => _connectedBodyA;
                set
                {
                    if (_connectedBodyA != null)
                    {
                        _connectedBodyA.ConstraintRemoved(this);
                    }
                    _connectedBodyA = value;
                    if (_connectedBodyA != null && IsActive)
                    {
                        _connectedBodyA.ConstraintAdded(this);
                    }
                    UpdateIndices();
                }
            }

            [SerializeIgnore]
            public Rigidbody BodyB
            {
                get => _connectedBodyB;
                set
                {
                    if (_connectedBodyB != null)
                    {
                        _connectedBodyB.ConstraintRemoved(this);
                    }
                    _connectedBodyB = value;
                    if (_connectedBodyB != null && IsActive)
                    {
                        _connectedBodyB.ConstraintAdded(this);
                    }
                    UpdateIndices();
                }
            }

            [Step(0.01f)]
            public Vector3 AnchorA { get; set; } = Vector3.Zero;

            [Step(0.01f)]
            public Vector3 AnchorB { get; set; } = Vector3.Zero;

            [Step(0.01f), Range(0.0f, 100.0f)]
            public float RestLength { get; set; } = 1.0f;

            [Step(0.001f), Range(0.0f, 1.0f)]
            public float Stiffness { get; set; } = 0.9f;

            [Step(0.001f), Range(0.0f, 1.0f)]
            public float Damping { get; set; } = 0.1f;

            [Step(0.001f), Range(0.0f, 1.0f)]
            public float ErrorReductionParameter { get; set; } = 0.2f;

            public ConstraintType Type { get; set; } = ConstraintType.Distance;

            public Vector3 Axis { get; set; } = Vector3.UnitY;
            public Vector3 Axis2 { get; set; } = Vector3.UnitZ;
            public Vector3 Axis3 { get; set; } = Vector3.UnitX;

            [Step(0.01f), Range(-MathF.PI, MathF.PI)]
            public float MinAngle { get; set; } = -MathF.PI / 4;

            [Step(0.01f), Range(-MathF.PI, MathF.PI)]
            public float MaxAngle { get; set; } = MathF.PI / 4;

            [Step(0.01f), Range(-10.0f, 10.0f)]
            public float MinDistance { get; set; } = -1.0f;

            [Step(0.01f), Range(-10.0f, 10.0f)]
            public float MaxDistance { get; set; } = 1.0f;

            [Step(0.01f), Range(0.0f, 10.0f)]
            public float TwistLimit { get; set; } = MathF.PI / 2;

            [Step(0.01f), Range(0.0f, MathF.PI)]
            public float SwingLimit1 { get; set; } = MathF.PI / 4;

            [Step(0.01f), Range(0.0f, MathF.PI)]
            public float SwingLimit2 { get; set; } = MathF.PI / 4;

            public bool UseSpring { get; set; } = false;

            [Step(0.1f), Range(0.0f, 1000.0f)]
            public float SpringStiffness { get; set; } = 100.0f;

            [Step(0.1f), Range(0.0f, 100.0f)]
            public float SpringDamping { get; set; } = 10.0f;

            [Step(0.01f), Range(0.0f, 1000.0f)]
            public float ImpulseClamp { get; set; } = 1000.0f;

            public ConstraintBreakMode BreakMode { get; set; } = ConstraintBreakMode.None;

            [Step(0.1f), Range(0.0f, 10000.0f)]
            public float BreakForce { get; set; } = 1000.0f;

            [Step(0.1f), Range(0.0f, 10000.0f)]
            public float BreakTorque { get; set; } = 1000.0f;

            [Step(0.01f), Range(0.0f, 10.0f)]
            public float BreakTime { get; set; } = 0.5f;

            public bool EnableCollision { get; set; } = false;

            [SerializeIgnore]
            public bool IsBroken => _isBroken;

            [SerializeIgnore]
            public float CurrentForce { get; private set; }

            [SerializeIgnore]
            public float CurrentTorque { get; private set; }

            [SerializeIgnore]
            public float Error { get; private set; }

            [SerializeIgnore]
            public uint ConstraintIndex { get; set; } = uint.MaxValue;

            #endregion

            #region Events

            public event Action<Constraint> OnConstraintBroken;
            public event Action<Constraint> OnConstraintRepaired;
            public event Action<Constraint, float> OnConstraintForceChanged;
            public event Action<Constraint, float> OnConstraintTorqueChanged;

            #endregion

            #region Constructors

            public Constraint() { }

            public Constraint(Rigidbody bodyA, Rigidbody bodyB)
            {
                BodyA = bodyA;
                BodyB = bodyB;
            }

            public Constraint(Rigidbody bodyA, Rigidbody bodyB, ConstraintType type)
            {
                BodyA = bodyA;
                BodyB = bodyB;
                Type = type;
            }

            #endregion

            #region Public Methods

            public void SetBodies(Rigidbody bodyA, Rigidbody bodyB, Vector3? anchorA = null, Vector3? anchorB = null)
            {
                BodyA = bodyA;
                BodyB = bodyB;

                if (anchorA.HasValue) AnchorA = anchorA.Value;
                if (anchorB.HasValue) AnchorB = anchorB.Value;

                // Auto-calculate rest length for distance constraints
                if (Type == ConstraintType.Distance || Type == ConstraintType.Spring)
                {
                    if (bodyA != null && bodyB != null)
                    {
                        var worldAnchorA = GetWorldAnchor(bodyA, AnchorA);
                        var worldAnchorB = GetWorldAnchor(bodyB, AnchorB);
                        RestLength = Vector3.Distance(worldAnchorA, worldAnchorB);
                    }
                }
            }

            public void BreakConstraint()
            {
                if (_isBroken) return;

                _isBroken = true;
                _breakTimer = 0.0f;

                // Notify physics manager
                var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
                physicsManager?.UnregisterConstraint(this);

                OnConstraintBroken?.Invoke(this);
            }

            public void RepairConstraint()
            {
                if (!_isBroken) return;

                _isBroken = false;
                _accumulatedImpulse = 0.0f;
                _accumulatedError = 0.0f;

                // Re-register with physics manager
                var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
                physicsManager?.RegisterConstraint(this);

                OnConstraintRepaired?.Invoke(this);
            }

            public Vector3 GetWorldAnchorA()
            {
                if (BodyA == null) return Vector3.Zero;
                return GetWorldAnchor(BodyA, AnchorA);
            }

            public Vector3 GetWorldAnchorB()
            {
                if (BodyB == null) return Vector3.Zero;
                return GetWorldAnchor(BodyB, AnchorB);
            }

            public void SetLimits(float min, float max)
            {
                switch (Type)
                {
                    case ConstraintType.Distance:
                    case ConstraintType.Prismatic:
                        MinDistance = min;
                        MaxDistance = max;
                        break;

                    case ConstraintType.Hinge:
                    case ConstraintType.ConeTwist:
                        MinAngle = min;
                        MaxAngle = max;
                        break;

                    case ConstraintType.Generic6DOF:
                        // For 6DOF, set all linear limits
                        MinDistance = min;
                        MaxDistance = max;
                        break;
                }
            }

            public void SetAngularLimits(float twist, float swing1, float swing2)
            {
                if (Type == ConstraintType.ConeTwist || Type == ConstraintType.Generic6DOF)
                {
                    TwistLimit = twist;
                    SwingLimit1 = swing1;
                    SwingLimit2 = swing2;
                }
            }

            #endregion

            #region Component Overrides

            public override ValueTask OnStart(WorldRenderer renderer)
            {
                var physicsManager = renderer.PhysicsManager;
                if (physicsManager != null && !_isBroken)
                {
                    physicsManager.RegisterConstraint(this);
                    UpdateIndices();
                }

                // Initialize last anchor positions
                _lastAnchorA = GetWorldAnchorA();
                _lastAnchorB = GetWorldAnchorB();

                return default;
            }


            public override async ValueTask Update(WorldRenderer renderer)
            {
                if (_isBroken && BreakTime > 0)
                {
                    _breakTimer += Time.DeltaTime;
                    if (_breakTimer >= BreakTime)
                    {
                        // Auto-repair after break time
                        RepairConstraint();
                    }
                }

                // Calculate approximate forces for monitoring
                if (!_isBroken && BodyA != null && BodyB != null)
                {
                    CalculateApproximateForces();
                }
            }

            public override void Destroy()
            {
                var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
                physicsManager?.UnregisterConstraint(this);

                // Remove from connected bodies
                BodyA?.ConstraintRemoved(this);
                BodyB?.ConstraintRemoved(this);

                base.Destroy();
            }

            public override void SetActive(bool isActive = true)
            {
                if (IsActive == isActive) return;

                base.SetActive(isActive);

                var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
                if (physicsManager != null)
                {
                    if (isActive && !_isBroken)
                    {
                        physicsManager.RegisterConstraint(this);
                    }
                    else
                    {
                        physicsManager.UnregisterConstraint(this);
                    }
                }
            }

            #endregion

            #region Internal Methods

            internal void UpdateConstraintData(ref GPUConstraint gpuConstraint)
            {
                uint bodyAIndex = BodyA?.Entity?.ID ?? uint.MaxValue;
                uint bodyBIndex = BodyB?.Entity?.ID ?? uint.MaxValue;

                // Try to get physics indices if available
                var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
                if (physicsManager != null)
                {
                    if (BodyA != null)
                    {
                        bodyAIndex = physicsManager.GetRigidbodyIndex(BodyA);
                    }
                    if (BodyB != null)
                    {
                        bodyBIndex = physicsManager.GetRigidbodyIndex(BodyB);
                    }
                }

                gpuConstraint = new GPUConstraint
                {
                    BodyA = bodyAIndex,
                    BodyB = bodyBIndex,
                    Stiffness = Stiffness,
                    Damping = Damping,
                    ConstraintType = (uint)Type,
                    MinDistance = MinDistance,
                    MaxDistance = MaxDistance,
                };
            }

            internal void OnPostPhysicsUpdate(float deltaTime)
            {
                // Update last anchor positions
                _lastAnchorA = GetWorldAnchorA();
                _lastAnchorB = GetWorldAnchorB();

                // Check if constraint should break
                if (!_isBroken && BreakMode != ConstraintBreakMode.None)
                {
                    CheckBreakCondition();
                }
            }

            #endregion

            #region Private Methods

            private void UpdateIndices()
            {
                var physicsManager = IoC.Container.GetInstance<PhysicsManager>();
                if (physicsManager != null)
                {
                    ConstraintIndex = physicsManager.GetConstraintIndex(this);
                }
            }

            private Vector3 GetWorldAnchor(Rigidbody body, Vector3 localAnchor)
            {
                if (body?.Entity?.Transform == null) return localAnchor;

                var transform = body.Entity.Transform;
                return Vector3.Transform(localAnchor, transform.WorldMatrix);
            }

            private void CalculateApproximateForces()
            {
                var worldAnchorA = GetWorldAnchorA();
                var worldAnchorB = GetWorldAnchorB();

                // Calculate current distance and direction
                Vector3 delta = worldAnchorB - worldAnchorA;
                float currentDistance = delta.Length();

                if (currentDistance > 0.001f)
                {
                    Vector3 direction = delta / currentDistance;

                    // Calculate constraint error
                    Error = currentDistance - RestLength;

                    // Approximate spring force (Hooke's law)
                    float springForce = 0.0f;
                    if (UseSpring && Type == ConstraintType.Spring)
                    {
                        springForce = -SpringStiffness * Error;
                    }

                    // Calculate relative velocity at anchors
                    Vector3 velA = BodyA.Velocity + Vector3.Cross(BodyA.AngularVelocity, worldAnchorA - BodyA.Entity.Transform.WorldPosition);
                    Vector3 velB = BodyB.Velocity + Vector3.Cross(BodyB.AngularVelocity, worldAnchorB - BodyB.Entity.Transform.WorldPosition);
                    Vector3 relativeVel = velB - velA;
                    float dampingForce = Vector3.Dot(relativeVel, direction) * Damping;

                    // Total approximate force
                    float totalForce = MathF.Abs(springForce + dampingForce);

                    // Calculate approximate torque (simplified)
                    float leverArm = MathF.Max(
                        Vector3.Distance(worldAnchorA, BodyA.Entity.Transform.WorldPosition),
                        Vector3.Distance(worldAnchorB, BodyB.Entity.Transform.WorldPosition));
                    float totalTorque = totalForce * leverArm;

                    // Update current values
                    if (MathF.Abs(CurrentForce - totalForce) > 0.01f)
                    {
                        CurrentForce = totalForce;
                        OnConstraintForceChanged?.Invoke(this, totalForce);
                    }

                    if (MathF.Abs(CurrentTorque - totalTorque) > 0.01f)
                    {
                        CurrentTorque = totalTorque;
                        OnConstraintTorqueChanged?.Invoke(this, totalTorque);
                    }
                }
            }
          
            private void CheckBreakCondition()
            {
                bool shouldBreak = false;

                switch (BreakMode)
                {
                    case ConstraintBreakMode.Force:
                        shouldBreak = CurrentForce > BreakForce;
                        break;

                    case ConstraintBreakMode.Torque:
                        shouldBreak = CurrentTorque > BreakTorque;
                        break;

                    case ConstraintBreakMode.ForceOrTorque:
                        shouldBreak = CurrentForce > BreakForce || CurrentTorque > BreakTorque;
                        break;
                }

                if (shouldBreak)
                {
                    BreakConstraint();
                }
            }

            #endregion
        }
    }
}