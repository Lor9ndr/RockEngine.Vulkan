using JoltPhysicsSharp;
using System.Numerics;
using System.Collections.Concurrent;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using NLog;
using System.Diagnostics;

namespace RockEngine.Core.Physics
{
    public enum BodyType
    {
        Static,
        Dynamic,
        Kinematic
    }

    public enum MotionQuality
    {
        Discrete,
        LinearCast
    }

    public struct PhysicsSettings
    {
        public int MaxBodies { get; set; }
        public int NumBodyMutexes { get; set; }
        public int MaxBodyPairs { get; set; }
        public int MaxContactConstraints { get; set; }
        public Vector3 Gravity { get; set; }

        public PhysicsSettings()
        {
            MaxBodies = 65536;
            NumBodyMutexes = 0;
            MaxBodyPairs = 65536;
            MaxContactConstraints = 65536;
            Gravity = new Vector3(0, -9.81f, 0);
        }
    }

    // Layers definitions
    public static class PhysicsLayers
    {
        public static readonly ObjectLayer NonMoving = 0;
        public static readonly ObjectLayer Moving = 1;
    }

    public static class PhysicsBroadPhaseLayers
    {
        public static readonly BroadPhaseLayer NonMoving = 0;
        public static readonly BroadPhaseLayer Moving = 1;
    }

    public class PhysicsManager : IDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static PhysicsManager? _instance;
        private PhysicsSettings _settings;
        private bool _isInitialized = false;

        // Jolt components
        private JobSystemThreadPool? _jobSystem;
        private PhysicsSystem? _physicsSystem;

        // Body management
        private readonly Dictionary<ulong, BodyID> _entityToBodyMap = new();
        private readonly Dictionary<BodyID, Entity> _bodyToEntityMap = new();
        private readonly ConcurrentQueue<PhysicsUpdate> _updateQueue = new();
        private readonly HashSet<BodyID> _ignoreDrawBodies = new();

        private readonly List<BodyID> _bodies = new();

        public static PhysicsManager Instance => _instance ??= new PhysicsManager();

        public PhysicsManager()
        {
            _settings = new PhysicsSettings();
            _instance = this;
        }

        public void Initialize(PhysicsSettings? settings = null)
        {
            if (_isInitialized)
                return;

            _settings = settings ?? new PhysicsSettings();

            try
            {
                // Initialize Jolt
                if (!Foundation.Init(false))
                {
                    throw new InvalidOperationException("Failed to initialize Jolt Physics");
                }

#if DEBUG
                bool d(string inExpression, string inMessage, string inFile, uint inLine)
                {
                    string message = inMessage ?? inExpression;
                    string outMessage = $"[JoltPhysics] Assertion failure at {inFile}:{inLine}: {message}";
                    Debug.WriteLine(outMessage);
                    _logger.Error(outMessage);
                    return true;
                }
                Foundation.SetAssertFailureHandler(d);
#endif
               
                // Setup collision filtering
                var systemSettings = SetupCollisionFiltering();

                // Create physics system
                _physicsSystem = new PhysicsSystem(systemSettings);

                // Set gravity
                _physicsSystem.Gravity = _settings.Gravity;

                // Create job system
                var jobSystemConfig = new JobSystemThreadPoolConfig();
                _jobSystem = new JobSystemThreadPool(jobSystemConfig);


                // Register event handlers
                _physicsSystem.OnContactValidate += OnContactValidate;
                _physicsSystem.OnContactAdded += OnContactAdded;
                _physicsSystem.OnContactPersisted += OnContactPersisted;
                _physicsSystem.OnContactRemoved += OnContactRemoved;
                _physicsSystem.OnBodyActivated += OnBodyActivated;
                _physicsSystem.OnBodyDeactivated += OnBodyDeactivated;

                _isInitialized = true;
                _logger.Info("Physics system initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize physics system");
                throw;
            }
        }

        private PhysicsSystemSettings SetupCollisionFiltering()
        {
            // We use only 2 layers: one for non-moving objects and one for moving objects
            var objectLayerPairFilter = new ObjectLayerPairFilterTable(2);
            objectLayerPairFilter.EnableCollision(PhysicsLayers.NonMoving, PhysicsLayers.Moving);
            objectLayerPairFilter.EnableCollision(PhysicsLayers.Moving, PhysicsLayers.Moving);

            // We use a 1-to-1 mapping between object layers and broadphase layers
            var broadPhaseLayerInterface = new BroadPhaseLayerInterfaceTable(2, 2);
            broadPhaseLayerInterface.MapObjectToBroadPhaseLayer(PhysicsLayers.NonMoving, PhysicsBroadPhaseLayers.NonMoving);
            broadPhaseLayerInterface.MapObjectToBroadPhaseLayer(PhysicsLayers.Moving, PhysicsBroadPhaseLayers.Moving);

            var objectVsBroadPhaseLayerFilter = new ObjectVsBroadPhaseLayerFilterTable(
                broadPhaseLayerInterface,
                2,
                objectLayerPairFilter,
                2
            );

            return new PhysicsSystemSettings()
            {
                MaxBodies = _settings.MaxBodies,
                MaxBodyPairs = _settings.MaxBodyPairs,
                MaxContactConstraints = _settings.MaxContactConstraints,
                NumBodyMutexes = _settings.NumBodyMutexes,
                ObjectLayerPairFilter = objectLayerPairFilter,
                BroadPhaseLayerInterface = broadPhaseLayerInterface,
                ObjectVsBroadPhaseLayerFilter = objectVsBroadPhaseLayerFilter
            };
        }

        public void Update(float deltaTime)
        {
            if (!_isInitialized || _physicsSystem == null ||  _jobSystem == null)
                return;

            try
            {
                // Process queued updates
                ProcessUpdateQueue();

                // Step the simulation
                const int collisionSteps = 4; // 1 collision step per frame
                var error = _physicsSystem.Update(deltaTime, collisionSteps, _jobSystem);

                if (error != PhysicsUpdateError.None)
                {
                    _logger.Warn($"Physics update error: {error}");
                }

                // Update entity transforms
                SynchronizeTransforms();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Physics update failed");
            }
        }

        private void ProcessUpdateQueue()
        {
            while (_updateQueue.TryDequeue(out var update))
            {
                ProcessPhysicsUpdate(update);
            }
        }

        private void ProcessPhysicsUpdate(PhysicsUpdate update)
        {
            CheckPhysicsInitialized();

            if ( !_entityToBodyMap.TryGetValue(update.EntityId, out var bodyId))
                return;

            try
            {
                switch (update.Type)
                {
                    case PhysicsUpdateType.SetPosition:
                        _physicsSystem!.BodyInterface.SetPosition(bodyId, update.Position.Value, Activation.Activate);
                        break;

                    case PhysicsUpdateType.SetRotation:
                        _physicsSystem!.BodyInterface.SetRotation(bodyId, update.Rotation.Value, Activation.Activate);
                        break;

                    case PhysicsUpdateType.SetLinearVelocity:
                        _physicsSystem!.BodyInterface.SetLinearVelocity(bodyId, update.LinearVelocity.Value);
                        break;

                    case PhysicsUpdateType.SetAngularVelocity:
                        _physicsSystem!.BodyInterface.SetAngularVelocity(bodyId, update.AngularVelocity.Value);
                        break;

                    case PhysicsUpdateType.AddForce:
                        _physicsSystem!.BodyInterface.AddForce(bodyId, update.Force.Value);
                        break;

                    case PhysicsUpdateType.AddImpulse:
                        _physicsSystem!.BodyInterface.AddImpulse(bodyId, update.Impulse.Value);
                        break;

                    case PhysicsUpdateType.SetFriction:
                        if (update.Force.HasValue)
                            _physicsSystem!.BodyInterface.SetFriction(bodyId, update.Force.Value.X);
                        break;

                    case PhysicsUpdateType.SetRestitution:
                        if (update.Force.HasValue)
                            _physicsSystem!.BodyInterface.SetRestitution(bodyId, update.Force.Value.Y);
                        break;

                   /* case PhysicsUpdateType.SetLinearDamping:
                        if (update.Force.HasValue)
                            _physicsSystem.BodyInterface.SetLinearDamping(bodyId, update.Force.Value.X);
                        break;

                    case PhysicsUpdateType.SetAngularDamping:
                        if (update.Force.HasValue)
                            _physicsSystem.BodyInterface.SetAngularDamping(bodyId, update.Force.Value.Y);
                        break;*/
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to process physics update");
            }
        }

        private void SynchronizeTransforms()
        {
            CheckPhysicsInitialized();

            // Update transforms for all active bodies
            foreach (var bodyId in _bodies)
            {
                if (_bodyToEntityMap.TryGetValue(bodyId, out var entity))
                {
                    var transform = entity.Transform;
                    if (transform == null)
                        continue;

                    // Get position and rotation from physics body
                    var position = _physicsSystem!.BodyInterface.GetPosition(bodyId);
                    var rotation = _physicsSystem!.BodyInterface.GetRotation(bodyId);

                    // Update transform (we need to avoid triggering the dirty flag)
                    UpdateTransformWithoutDirty(transform, position, rotation);
                }
            }
        }

        private void UpdateTransformWithoutDirty(Transform transform, Vector3 position, Quaternion rotation)
        {
            // We need to update the transform without triggering the dirty flag
            // This is a workaround - in a real implementation, you might want to 
            // modify Transform class to support this properly
            var positionChanged = transform.Position != position;
            var rotationChanged = transform.Rotation != rotation;

            if (positionChanged || rotationChanged)
            {
                // Use reflection to set private fields
                var positionField = typeof(Transform).GetField("_position",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var rotationField = typeof(Transform).GetField("_rotation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var worldMatrixField = typeof(Transform).GetField("_worldMatrix",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var isDirtyField = typeof(Transform).GetField("_isDirty",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                //positionField?.SetValue(transform, position);
                //rotationField?.SetValue(transform, rotation);
                transform.Position = position;
                transform.Rotation = rotation;
                //isDirtyField?.SetValue(transform, true);
                //worldMatrixField?.SetValue(transform, transform.WorldMatrix); // Force recompute

            }
        }

        public BodyID CreateRigidBody(Entity entity, BodyCreationSettings creationSettings)
        {
            CheckPhysicsInitialized();

            var bodyId = _physicsSystem!.BodyInterface.CreateAndAddBody(creationSettings, Activation.Activate);

            _entityToBodyMap[entity.ID] = bodyId;
            _bodyToEntityMap[bodyId] = entity;
            _bodies.Add(bodyId);

            return bodyId;
        }

        public void RemoveRigidBody(Entity entity)
        {
            if (_physicsSystem is null
                || _physicsSystem.IsDisposed
                || _physicsSystem.BodyInterface.IsNull
                || !_entityToBodyMap.TryGetValue(entity.ID, out var bodyId))
                return;

            _physicsSystem.BodyInterface.RemoveAndDestroyBody(bodyId);

            _entityToBodyMap.Remove(entity.ID);
            _bodyToEntityMap.Remove(bodyId);
            _bodies.Remove(bodyId);
        }

        public void QueueUpdate(ulong entityId, PhysicsUpdate update)
        {
            update.EntityId = entityId;
            _updateQueue.Enqueue(update);
        }

        public RayCastResult RayCast(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (_physicsSystem == null)
                return new RayCastResult();

            try
            {
                var ray = new JoltPhysicsSharp.Ray
                {
                    Position = origin,
                    Direction = direction
                };

                var settings = new RayCastSettings();
                var collector = CollisionCollectorType.ClosestHit;
                var results = new JoltPhysicsSharp.RayCastResult[1];
                

                if (_physicsSystem.NarrowPhaseQuery.CastRay(ray, settings, collector, results))
                {
                    var hit = results[0];
                    return new RayCastResult
                    {
                        Hit = true,
                        HitEntity = _bodyToEntityMap.GetValueOrDefault(hit.BodyID),
                        Distance = hit.Fraction * maxDistance
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Raycast failed");
            }

            return new RayCastResult();
        }

        public BodyID CreateBox(Vector3 halfExtent, Vector3 position, Quaternion rotation,
            MotionType motionType, ObjectLayer layer, Activation activation = Activation.Activate)
        {
            CheckPhysicsInitialized();

            var shape = new BoxShape(halfExtent);
            var creationSettings = new BodyCreationSettings(shape, position, rotation, motionType, layer);

            var bodyId = _physicsSystem!.BodyInterface.CreateAndAddBody(creationSettings, activation);
            _bodies.Add(bodyId);

            return bodyId;
        }

      

        public BodyID CreateSphere(float radius, Vector3 position, Quaternion rotation,
            MotionType motionType, ObjectLayer layer, Activation activation = Activation.Activate)
        {
            CheckPhysicsInitialized();

            var shape = new SphereShape(radius);
            var creationSettings = new BodyCreationSettings(shape, position, rotation, motionType, layer);

            var bodyId = _physicsSystem!.BodyInterface.CreateAndAddBody(creationSettings, activation);
            _bodies.Add(bodyId);

            return bodyId;
        }

        public BodyID CreateFloor(float size, ObjectLayer layer)
        {
            CheckPhysicsInitialized();

            var shape = new BoxShape(new Vector3(size, 5.0f, size));
            var creationSettings = new BodyCreationSettings(shape, new Vector3(0, -5.0f, 0),
                Quaternion.Identity, MotionType.Static, layer);

            var bodyId = _physicsSystem!.BodyInterface.CreateAndAddBody(creationSettings, Activation.DontActivate);
            _bodies.Add(bodyId);
            _ignoreDrawBodies.Add(bodyId);

            return bodyId;
        }

        public void OptimizeBroadPhase()
        {
            _physicsSystem?.OptimizeBroadPhase();
        }
        private void CheckPhysicsInitialized()
        {
            if (_physicsSystem is null
                            || _physicsSystem.IsDisposed
                            || _physicsSystem.BodyInterface.IsNull)
            {
                throw new InvalidOperationException("Physics system not initialized");
            }
        }

        #region Event Handlers
        private ValidateResult OnContactValidate(PhysicsSystem system, in Body body1, in Body body2,
            RVector3 baseOffset, in CollideShapeResult collisionResult)
        {
            _logger.Debug("Contact validate callback");
            return ValidateResult.AcceptAllContactsForThisBodyPair;
        }

        private void OnContactAdded(PhysicsSystem system, in Body body1, in Body body2,
            in ContactManifold manifold, ref ContactSettings settings)
        {
            _logger.Debug("A contact was added");
        }

        private void OnContactPersisted(PhysicsSystem system, in Body body1, in Body body2,
            in ContactManifold manifold, ref ContactSettings settings)
        {
            settings.CombinedRestitution = 0.5f;
            _logger.Debug("A contact was persisted");
        }

        private void OnContactRemoved(PhysicsSystem system, ref SubShapeIDPair subShapePair)
        {
            _logger.Debug("A contact was removed");
        }

        private void OnBodyActivated(PhysicsSystem system, in BodyID bodyID, ulong bodyUserData)
        {
            _logger.Debug("A body got activated");
        }

        private void OnBodyDeactivated(PhysicsSystem system, in BodyID bodyID, ulong bodyUserData)
        {
            _logger.Debug("A body went to sleep");
        }
        #endregion

        public void Dispose()
        {
            try
            {
                _isInitialized = false;
                // Clean up all bodies
                foreach (var bodyId in _bodies.ToArray())
                {
                    try
                    {
                        _physicsSystem.BodyInterface.RemoveAndDestroyBody(bodyId);
                    }
                    catch { }
                }

                _bodies.Clear();
                _entityToBodyMap.Clear();
                _bodyToEntityMap.Clear();
                _ignoreDrawBodies.Clear();
                while (_updateQueue.TryDequeue(out _)) { }

                // Dispose Jolt components
                _jobSystem?.Dispose();
                _physicsSystem?.Dispose();

                Foundation.Shutdown();

                _logger.Info("Physics system disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disposing physics system");
            }
        }
    }

    public struct PhysicsUpdate
    {
        public ulong EntityId;
        public PhysicsUpdateType Type;
        public Vector3? Position;
        public Quaternion? Rotation;
        public Vector3? LinearVelocity;
        public Vector3? AngularVelocity;
        public Vector3? Force;
        public Vector3? Impulse;
    }

    public enum PhysicsUpdateType
    {
        SetPosition,
        SetRotation,
        SetLinearVelocity,
        SetAngularVelocity,
        AddForce,
        AddImpulse,
        SetFriction,
        SetRestitution,
        SetLinearDamping,
        SetAngularDamping
    }

    public struct RayCastResult
    {
        public bool Hit;
        public Entity? HitEntity;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public float Distance;
    }
}