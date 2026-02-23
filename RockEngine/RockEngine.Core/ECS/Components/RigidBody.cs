using JoltPhysicsSharp;

using System.Numerics;

using RockEngine.Core.Rendering;
using RockEngine.Core.Physics;

using MessagePack;

namespace RockEngine.Core.ECS.Components.Physics
{
    [MessagePackObject(AllowPrivate = true)]
    public partial class RigidbodyComponent : Component, IDisposable
    {
        [IgnoreMember]
        private readonly PhysicsManager _physicsManager;
        [IgnoreMember]

        private BodyID? _bodyId;
        [IgnoreMember]

        private Core.Physics.BodyType _bodyType = Core.Physics.BodyType.Dynamic;
        [IgnoreMember]

        private Core.Physics.MotionQuality _motionQuality = Core.Physics.MotionQuality.Discrete;
        [IgnoreMember]

        private float _mass = 1.0f;
        [IgnoreMember]

        private float _friction = 0.5f;
        [IgnoreMember]

        private float _restitution = 0.3f;
        [IgnoreMember]

        private float _linearDamping = 0.05f;
        [IgnoreMember]

        private float _angularDamping = 0.05f;
        [IgnoreMember]

        private Vector3 _lastPosition;
        [IgnoreMember]

        private Quaternion _lastRotation;
        [IgnoreMember]

        private bool _isDirty = true;
        [IgnoreMember]

        private Core.Physics.BodyType _lastBodyType = Core.Physics.BodyType.Dynamic;
        [IgnoreMember]

        private Core.Physics.MotionQuality _lastMotionQuality = Core.Physics.MotionQuality.Discrete;
        [IgnoreMember]

        private List<IColliderComponent> _colliders = new();
        [IgnoreMember]

        private Shape? _cachedShape;
        [IgnoreMember]

        private bool _isDisposed;

        [Key(24)]
        public Core.Physics.BodyType BodyType
        {
            get => _bodyType;
            set
            {
                if (_bodyType != value)
                {
                    _bodyType = value;
                    MarkDirty();
                }
            }
        }

        [Key(25)]
        public Core.Physics.MotionQuality MotionQuality
        {
            get => _motionQuality;
            set
            {
                if (_motionQuality != value)
                {
                    _motionQuality = value;
                    MarkDirty();
                }
            }
        }

        [Key(26)]
        public float Mass
        {
            get => _mass;
            set
            {
                if (Math.Abs(_mass - value) > 0.001f)
                {
                    _mass = Math.Max(0.001f, value);
                    MarkDirty();
                }
            }
        }

        [Key(27)]
        public float Friction
        {
            get => _friction;
            set
            {
                _friction = Math.Clamp(value, 0.0f, 1.0f);
                UpdateFriction();
            }
        }

        [Key(28)]
        public float Restitution
        {
            get => _restitution;
            set
            {
                _restitution = Math.Clamp(value, 0.0f, 1.0f);
                UpdateRestitution();
            }
        }

        [Key(29)]
        public float LinearDamping
        {
            get => _linearDamping;
            set
            {
                _linearDamping = Math.Max(0.0f, value);
                UpdateLinearDamping();
            }
        }

        [Key(30)]
        public float AngularDamping
        {
            get => _angularDamping;
            set
            {
                _angularDamping = Math.Max(0.0f, value);
                UpdateAngularDamping();
            }
        }

        [Key(31)]
        public Vector3 LinearVelocity { get; set; }
        [Key(32)]
        public Vector3 AngularVelocity { get; set; }

        [Key(33)]
        public IReadOnlyList<IColliderComponent> Colliders => _colliders;

        public RigidbodyComponent()
        {
            _physicsManager = PhysicsManager.Instance;
        }

        public override async ValueTask OnStart(WorldRenderer renderer)
        {
            if (Entity == null)
                return;

            await base.OnStart(renderer);

            // Найти все коллайдеры и подписаться на их изменения
            FindAndSubscribeToColliders();

            var transform = Entity.GetComponent<Transform>();
            if (transform == null)
                return;

            CreatePhysicsBody(transform);

            _lastPosition = transform.WorldPosition;
            _lastRotation = transform.WorldRotation;
            _lastBodyType = _bodyType;
            _lastMotionQuality = _motionQuality;
        }

        private void FindAndSubscribeToColliders()
        {
            // Отписаться от старых коллайдеров
            UnsubscribeFromAllColliders();

            // Найти все коллайдеры
            _colliders.Clear();

            foreach (var component in Entity!.Components)
            {
                if (component is IColliderComponent collider)
                {
                    _colliders.Add(collider);
                    collider.OnChanged += OnColliderChanged;
                }
            }
        }

        private void UnsubscribeFromAllColliders()
        {
            foreach (var collider in _colliders)
            {
                collider.OnChanged -= OnColliderChanged;
            }
            _colliders.Clear();
        }

        private void OnColliderChanged(IColliderComponent collider)
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            _isDirty = true;
            _cachedShape = null; // Инвалидировать кешированную форму
        }

        private void CreatePhysicsBody(Transform transform)
        {
            // Удалить существующее тело
            if (_bodyId.HasValue)
            {
                try
                {
                    _physicsManager.RemoveRigidBody(Entity!);
                }
                catch { }
                _bodyId = null;
            }

            // Создать форму
            var shape = CreateCollisionShape();
            if (shape == null)
            {
                shape = new BoxShape(new Vector3(0.5f, 0.5f, 0.5f));
            }

            // Определить тип движения
            var motionType = _bodyType switch
            {
                Core.Physics.BodyType.Static => MotionType.Static,
                Core.Physics.BodyType.Kinematic => MotionType.Kinematic,
                _ => MotionType.Dynamic,
            };

            // Определить слой
            var layer = motionType == MotionType.Static ?
                PhysicsLayers.NonMoving : PhysicsLayers.Moving;

            // Создать настройки тела
            var settings = new BodyCreationSettings(
                shape,
                transform.WorldPosition,
                transform.WorldRotation,
                motionType,
                layer
            );

            // Установить качество движения
            settings.MotionQuality = _motionQuality == Core.Physics.MotionQuality.LinearCast ?
                 JoltPhysicsSharp.MotionQuality.LinearCast : JoltPhysicsSharp.MotionQuality.Discrete;

            // Установить массу и другие свойства для динамических тел
            if (motionType == MotionType.Dynamic)
            {
                settings.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
                var massProps = settings.MassPropertiesOverride;
                massProps.Mass = _mass;
                settings.MassPropertiesOverride = massProps;
                settings.Restitution = _restitution;
                settings.Friction = _friction;
                settings.LinearDamping = _linearDamping;
                settings.AngularDamping = _angularDamping;
            }
            else
            {
                // Для статических и кинематических тел установить базовые свойства
                settings.Restitution = _restitution;
                settings.Friction = _friction;
            }

            // Создать тело
            _bodyId = _physicsManager.CreateRigidBody(Entity!, settings);
            _cachedShape = shape; // Кешировать форму
        }

        private Shape? CreateCollisionShape()
        {
            if (_colliders.Count == 0)
                return null;

            // Если только один коллайдер
            if (_colliders.Count == 1)
            {
                var collider = _colliders[0];
                if (collider is ColliderComponent shapeCollider)
                {
                    return shapeCollider.CreateShape();
                }
                return null;
            }
            throw new NotImplementedException("Multiple colliders not supported for now");
        }


        public override async ValueTask Update(WorldRenderer renderer)
        {
            if (Entity == null || !IsActive || _isDisposed)
                return;

            var transform = Entity.GetComponent<Transform>();
            if (transform == null)
                return;

            // Пересоздать тело, если изменились важные параметры
            if (_isDirty || _bodyType != _lastBodyType || _motionQuality != _lastMotionQuality)
            {
                CreatePhysicsBody(transform);
                _lastBodyType = _bodyType;
                _lastMotionQuality = _motionQuality;
                _isDirty = false;
            }

            // Обновить физику, если изменилась трансформация (для кинематических тел)
            if (_bodyId.HasValue)
            {
                if (transform.WorldPosition != _lastPosition || transform.WorldRotation != _lastRotation)
                {
                    _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
                    {
                        Type = PhysicsUpdateType.SetPosition,
                        Position = transform.WorldPosition
                    });

                    _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
                    {
                        Type = PhysicsUpdateType.SetRotation,
                        Rotation = transform.WorldRotation
                    });

                    _lastPosition = transform.WorldPosition;
                    _lastRotation = transform.WorldRotation;
                }
            }

            await base.Update(renderer);
        }

        private void UpdateFriction()
        {
            if (_bodyId.HasValue && Entity != null)
            {
                _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
                {
                    Type = PhysicsUpdateType.SetFriction,
                    Force = new Vector3(_friction, 0, 0)
                });
            }
        }

        private void UpdateRestitution()
        {
            if (_bodyId.HasValue && Entity != null)
            {
                _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
                {
                    Type = PhysicsUpdateType.SetRestitution,
                    Force = new Vector3(0, _restitution, 0)
                });
            }
        }

        private void UpdateLinearDamping()
        {
            if (_bodyId.HasValue && Entity != null)
            {
                _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
                {
                    Type = PhysicsUpdateType.SetLinearDamping,
                    Force = new Vector3(_linearDamping, 0, 0)
                });
            }
        }

        private void UpdateAngularDamping()
        {
            if (_bodyId.HasValue && Entity != null)
            {
                _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
                {
                    Type = PhysicsUpdateType.SetAngularDamping,
                    Force = new Vector3(0, _angularDamping, 0)
                });
            }
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (_bodyId == null || Entity == null)
                return;

            _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
            {
                Type = mode == ForceMode.Impulse ?
                    PhysicsUpdateType.AddImpulse : PhysicsUpdateType.AddForce,
                Force = force
            });
        }

        public void SetVelocity(Vector3 linear, Vector3 angular)
        {
            if (_bodyId == null || Entity == null)
                return;

            LinearVelocity = linear;
            AngularVelocity = angular;

            _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
            {
                Type = PhysicsUpdateType.SetLinearVelocity,
                LinearVelocity = linear
            });

            _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
            {
                Type = PhysicsUpdateType.SetAngularVelocity,
                AngularVelocity = angular
            });
        }

        public void WakeUp()
        {
            if (_bodyId == null || Entity == null)
                return;

            _physicsManager.QueueUpdate(Entity.ID, new PhysicsUpdate
            {
                Type = PhysicsUpdateType.SetPosition,
                Position = Entity.GetComponent<Transform>()!.Position
            });
        }

        public override void Destroy()
        {
            Dispose();
            base.Destroy();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (Entity != null && _bodyId.HasValue)
            {
                _physicsManager.RemoveRigidBody(Entity);
                _bodyId = null;
            }

            UnsubscribeFromAllColliders();
            _colliders.Clear();
            _cachedShape = null;
        }
    }

    public enum ForceMode
    {
        Force,
        Impulse
    }
}