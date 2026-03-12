using NLog;

using RockEngine.Core.ECS.Components;
using RockEngine.Core.ECS.Components.RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
    public partial class PhysicsManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly ComputeShaderManager _computeManager;
        private readonly BindingManager _bindingManager;

        private RckPipeline _physicsIntegratePipeline;
        private RckPipeline _collisionDetectionPipeline;
        private RckPipeline _collisionResponsePipeline;
        private RckPipeline _constraintSolverPipeline;
        private RckPipeline _particleUpdatePipeline;
        private RckPipeline _particleEmitPipeline;
        private RckPipeline _broadphasePipeline;
        private RckPipeline _updatePositionsPipeline;
        private RckPipeline _gjkPipeline;
        private RckPipeline _epaPipeline;
        private RckPipeline _satPipeline;
        private RckPipeline _ccdPipeline;
        private RckPipeline _contactGenerationPipeline;
        private RckPipeline _frictionSolverPipeline;

        private readonly List<Rigidbody> _rigidbodies = new();
        private readonly List<Collider> _colliders = new();
        private readonly List<ParticleSystem> _particleSystems = new();
        private readonly List<Constraint> _constraints = new();
        private uint _nextConstraintIndex = 0;

        // Main buffers
        private StorageBuffer<GPURigidbody> _rigidbodyBuffer;
        private StorageBuffer<GPUCollider> _colliderBuffer;
        private StorageBuffer<GPUParticle> _particleBuffer;
        private StorageBuffer<GPUConstraint> _constraintBuffer;
        private StorageBuffer<CollisionPair> _collisionPairBuffer;
        private StorageBuffer<BroadphaseCell> _broadphaseCellBuffer;
        private StorageBuffer<BroadphaseObject> _broadphaseObjectBuffer;
        private StorageBuffer<Manifold> _contactManifoldBuffer;
        private StorageBuffer<uint> _broadphaseCounterBuffer;
        private StorageBuffer<uint> _collisionCounterBuffer;
        private VkBuffer _collisionCounterStagingBuffer;
        private UniformBuffer _physicsParamsBuffer;

        // Advanced collision buffers
        private StorageBuffer<GJKSimplex> _gjkSimplexBuffer;
        private StorageBuffer<EPATriangle> _epaTriangleBuffer;
        private StorageBuffer<SATResult> _satResultBuffer;
        private StorageBuffer<CCDResult> _ccdResultBuffer;
        private StorageBuffer<ContactPoint> _contactPointBuffer;
        private StorageBuffer<PersistentContact> _persistentContactBuffer;
        private StorageBuffer<CollisionResolutionData> _collisionResolutionBuffer;
        private VkBuffer _collisionResolutionStagingBuffer;
        private CollisionResolutionData[] _collisionResolutionCache;

        // Staging buffers for readback
        private VkBuffer _rigidbodyStagingBuffer;
        private VkBuffer _particleStagingBuffer;
        private VkBuffer _colliderStagingBuffer;
        private VkBuffer _collisionPairStagingBuffer;
        private VkBuffer _gjkStagingBuffer;
        private VkBuffer _contactPointStagingBuffer;

        // Readback caches
        private GPURigidbody[] _rigidbodyReadbackCache;
        private GPUParticle[] _particleReadbackCache;
        private GPUCollider[] _colliderReadbackCache;
        private CollisionPair[] _collisionPairCache;
        private ContactPoint[] _contactPointCache;
        private GJKSimplex[] _gjkSimplexCache;

        private bool _isInitialized = false;
        private readonly ConcurrentQueue<ParticleEmissionRequest> _emissionQueue = new();
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Physics constants
        private const uint SOLVER_ITERATIONS = 8;
        private const uint COLLISION_ITERATIONS = 6;
        private const uint FRICTION_ITERATIONS = 4;
        private const uint CCD_MAX_STEPS = 8;
        private const float FIXED_TIMESTEP = 1.0f / 120.0f;
        private const float BAUMGARTE_FACTOR = 0.2f;
        private const float BAUMGARTE_SLOP = 0.01f;
        private const float MAX_CORRECTION = 0.2f;
        private const float CCD_THRESHOLD = 0.1f;
        private const float CONTACT_THRESHOLD = 0.01f;
        private const float PERSISTENT_THRESHOLD = 0.005f;
        private const float CONTACT_MERGE_DISTANCE = 0.02f;
        private const float NORMAL_TOLERANCE = 0.01f;
        private const float WARM_START_FACTOR = 0.8f;
        private const float RELAXATION_FACTOR = 0.75f;
        private const float STATIC_FRICTION_THRESHOLD = 0.5f;
        private const float DYNAMIC_FRICTION_SCALE = 0.8f;
        private const float FRICTION_STIFFNESS = 0.3f;
        private const float EXPANSION_FACTOR = 1.1f;
        private const uint MAX_CONTACT_POINTS_PER_PAIR = 4;
        private const uint MAX_SIMPLEX_SIZE = 64;
        private const uint MAX_EPA_TRIANGLES = 256;
        private const uint MAX_FACE_CHECKS = 15;
        private const uint MAX_EDGE_CHECKS = 30;
        private const uint MAX_GJK_ITERATIONS = 32;
        private const uint MAX_EPA_ITERATIONS = 64;
        private const float GJK_EPSILON = 1e-4f;
        private const float EPA_EPSILON = 1e-4f;
        private const float SAT_EPSILON = 1e-4f;
        private const float FEATURE_SWAP_THRESHOLD = 0.98f;
        private const float DEPTH_TOLERANCE = 0.001f;
        private const float IMPULSE_SLOP = 0.01f;
        private const float VELOCITY_THRESHOLD = 0.01f;
        private const float ANGULAR_THRESHOLD = 0.1f;

        private float _accumulator = 0.0f;
        private bool _constraintsDirty;
        private int _lastConstraintCount;
        private int _maxCollisionPairs;

        // Synchronization
        private VkFence _physicsFence;
        private VkSemaphore _physicsSemaphore;
        private FlushOperation? _flushOp;

        // Collision event tracking
        private HashSet<(uint, uint)> _previousFrameCollisions = new();
        private HashSet<(uint, uint)> _currentFrameCollisions = new();
        private uint _lastCollisionCount;
        private readonly List<CollisionEvent> _collisionEvents = new();

        // Performance tracking
        private bool _useSleeping = true;
        private float _sleepThreshold = 0.1f;
        private float _sleepTimeThreshold = 2.0f;
        private Dictionary<uint, float> _sleepTimers = new();

        // Persistent contact caching
        private Dictionary<(uint, uint), PersistentManifold> _persistentManifolds = new();
        private uint _persistentManifoldAge = 0;
        private const int MAX_PERSISTENT_AGE = 5;

        // Contact warm starting
        private Dictionary<ulong, ContactWarmStartData> _warmStartData = new();
        private UniformBuffer _solverParamsBuffer;

        public bool IsInitialized => _isInitialized;
        public Vector3 Gravity { get; set; } = new Vector3(0, -9.81f, 0);
        public uint MaxParticles { get; set; } = 10000;
        public uint MaxConstraints { get; set; } = 1000;
        public uint MaxRigidbodies { get; set; } = 100;
        public uint MaxCollisionPairs { get; set; } = 1000;
        public uint MaxContactPoints { get; set; } = 4000; // MaxCollisionPairs * MAX_CONTACT_POINTS_PER_PAIR
        public uint BroadphaseGridSize { get; set; } = 1000;
        public float BroadphaseCellSize { get; set; } = 2.0f;
        public uint MaxObjectsPerCell { get; set; } = 32;
        public uint MaxSimplexes { get; set; } = 1000;
        public uint MaxEPATriangles { get; set; } = 10000;
        public uint MaxSATResults { get; set; } = 1000;
        public uint MaxCCDResults { get; set; } = 100;

        // Collision events
        public event Action<CollisionEvent> OnCollisionEnter;
        public event Action<CollisionEvent> OnCollisionStay;
        public event Action<CollisionEvent> OnCollisionExit;

        public PhysicsManager(
            VulkanContext context,
            ComputeShaderManager computeManager,
            BindingManager bindingManager)
        {
            _context = context;
            _computeManager = computeManager;
            _bindingManager = bindingManager;
            _maxCollisionPairs = (int)MaxCollisionPairs;
            _collisionPairCache = new CollisionPair[_maxCollisionPairs];
            _contactPointCache = new ContactPoint[MaxContactPoints];
            _gjkSimplexCache = new GJKSimplex[MaxSimplexes];
        }

        public async Task InitializeAsync()
        {
            try
            {
                _logger.Info("Initializing PhysicsManager...");

                // Create compute pipelines
                await CreateComputePipelines();

                // Create buffers
                await CreateBuffers();

                // Create staging buffers
                await CreateStagingBuffers();

                // Initialize readback caches
                InitializeReadbackCaches();

                // Create synchronization objects
                CreateSynchronizationObjects();

                // Initialize all buffers
                await InitializeAllBuffers();

                _isInitialized = true;
                _logger.Info($"PhysicsManager initialized with {MaxRigidbodies} max rigidbodies, {MaxCollisionPairs} max collision pairs");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize PhysicsManager");
                throw;
            }
        }

        private async Task CreateComputePipelines()
        {
            _physicsIntegratePipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\physics_integrate.comp.spv",
                "PhysicsIntegrate");

            _collisionDetectionPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\collision_detection.comp.spv",
                "CollisionDetection");

            _collisionResponsePipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\collision_response.comp.spv",
                "CollisionResponse");

            _constraintSolverPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\constraint_solver.comp.spv",
                "ConstraintSolver");

            _particleUpdatePipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\particle_update.comp.spv",
                "ParticleUpdate");

            _particleEmitPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\particle_emit.comp.spv",
                "ParticleEmit");

            _broadphasePipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\broadphase.comp.spv",
                "Broadphase");

            _updatePositionsPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\update_positions.comp.spv",
                "UpdatePositions");

            _gjkPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\gjk_epa.comp.spv",
                "GJKEPA");

            _epaPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\epa_solver.comp.spv",
                "EPASolver");

            _satPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\sat.comp.spv",
                "SAT");

            _ccdPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\ccd.comp.spv",
                "CCD");

            _contactGenerationPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\contact_generation.comp.spv",
                "ContactGeneration");

            _frictionSolverPipeline = await _computeManager.CreateComputePipelineAsync(
                "Shaders\\Compute\\Physics\\friction_solver.comp.spv",
                "FrictionSolver");
        }

        private async Task CreateBuffers()
        {
            uint rigidbodyBufferSize = (uint)Unsafe.SizeOf<GPURigidbody>() * MaxRigidbodies;
            uint colliderBufferSize = (uint)Unsafe.SizeOf<GPUCollider>() * MaxRigidbodies;
            uint particleBufferSize = (uint)Unsafe.SizeOf<GPUParticle>() * MaxParticles;
            uint constraintBufferSize = (uint)Unsafe.SizeOf<GPUConstraint>() * MaxConstraints;
            uint collisionPairBufferSize = (uint)Unsafe.SizeOf<CollisionPair>() * MaxCollisionPairs;
            uint manifoldBufferSize = (uint)Unsafe.SizeOf<Manifold>() * MaxCollisionPairs;
            uint broadphaseCellBufferSize = (uint)Unsafe.SizeOf<BroadphaseCell>() * BroadphaseGridSize;
            uint broadphaseObjectBufferSize = (uint)Unsafe.SizeOf<BroadphaseObject>() * MaxRigidbodies * 27;
            uint counterBufferSize = sizeof(uint) * 2;
            uint gjkSimplexBufferSize = (uint)Unsafe.SizeOf<GJKSimplex>() * MaxSimplexes;
            uint epaTriangleBufferSize = (uint)Unsafe.SizeOf<EPATriangle>() * MaxEPATriangles;
            uint satResultBufferSize = (uint)Unsafe.SizeOf<SATResult>() * MaxSATResults;
            uint ccdResultBufferSize = (uint)Unsafe.SizeOf<CCDResult>() * MaxCCDResults;
            uint contactPointBufferSize = (uint)Unsafe.SizeOf<ContactPoint>() * MaxContactPoints;
            uint persistentContactBufferSize = (uint)Unsafe.SizeOf<PersistentContact>() * MaxContactPoints;
            uint collisionResolutionBufferSize = (uint)Unsafe.SizeOf<CollisionResolutionData>() * MaxCollisionPairs;

            _rigidbodyBuffer = new StorageBuffer<GPURigidbody>(_context, rigidbodyBufferSize);
            _colliderBuffer = new StorageBuffer<GPUCollider>(_context, colliderBufferSize);
            _particleBuffer = new StorageBuffer<GPUParticle>(_context, particleBufferSize);
            _constraintBuffer = new StorageBuffer<GPUConstraint>(_context, constraintBufferSize);
            _collisionPairBuffer = new StorageBuffer<CollisionPair>(_context, collisionPairBufferSize);
            _collisionCounterBuffer = new StorageBuffer<uint>(_context, counterBufferSize);
            _contactManifoldBuffer = new StorageBuffer<Manifold>(_context, manifoldBufferSize);
            _broadphaseCellBuffer = new StorageBuffer<BroadphaseCell>(_context, broadphaseCellBufferSize);
            _broadphaseObjectBuffer = new StorageBuffer<BroadphaseObject>(_context, broadphaseObjectBufferSize);
            _broadphaseCounterBuffer = new StorageBuffer<uint>(_context, counterBufferSize);
            _gjkSimplexBuffer = new StorageBuffer<GJKSimplex>(_context, gjkSimplexBufferSize);
            _epaTriangleBuffer = new StorageBuffer<EPATriangle>(_context, epaTriangleBufferSize);
            _satResultBuffer = new StorageBuffer<SATResult>(_context, satResultBufferSize);
            _ccdResultBuffer = new StorageBuffer<CCDResult>(_context, ccdResultBufferSize);
            _contactPointBuffer = new StorageBuffer<ContactPoint>(_context, contactPointBufferSize);
            _persistentContactBuffer = new StorageBuffer<PersistentContact>(_context, persistentContactBufferSize);
            _physicsParamsBuffer = new UniformBuffer(_context, (uint)Unsafe.SizeOf<PhysicsParams>());
            _collisionResolutionBuffer = new StorageBuffer<CollisionResolutionData>(_context, collisionResolutionBufferSize);
        }

        private async Task CreateStagingBuffers()
        {
            uint rigidbodyBufferSize = (uint)Unsafe.SizeOf<GPURigidbody>() * MaxRigidbodies;
            uint colliderBufferSize = (uint)Unsafe.SizeOf<GPUCollider>() * MaxRigidbodies;
            uint particleBufferSize = (uint)Unsafe.SizeOf<GPUParticle>() * MaxParticles;
            uint collisionPairBufferSize = (uint)Unsafe.SizeOf<CollisionPair>() * MaxCollisionPairs;
            uint contactPointBufferSize = (uint)Unsafe.SizeOf<ContactPoint>() * MaxContactPoints;
            uint gjkSimplexBufferSize = (uint)Unsafe.SizeOf<GJKSimplex>() * MaxSimplexes;
            uint counterBufferSize = sizeof(uint) * 2;
            uint collisionResolutionBufferSize = (uint)Unsafe.SizeOf<CollisionResolutionData>() * MaxCollisionPairs;

            _rigidbodyStagingBuffer = VkBuffer.Create(_context, rigidbodyBufferSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _colliderStagingBuffer = VkBuffer.Create(_context, colliderBufferSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _particleStagingBuffer = VkBuffer.Create(_context, particleBufferSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _collisionPairStagingBuffer = VkBuffer.Create(_context, collisionPairBufferSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _contactPointStagingBuffer = VkBuffer.Create(_context, contactPointBufferSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _gjkStagingBuffer = VkBuffer.Create(_context, gjkSimplexBufferSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _collisionCounterStagingBuffer = VkBuffer.Create(_context, counterBufferSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _collisionResolutionStagingBuffer = VkBuffer.Create(_context, collisionResolutionBufferSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        private void InitializeReadbackCaches()
        {
            _rigidbodyReadbackCache = new GPURigidbody[MaxRigidbodies];
            _colliderReadbackCache = new GPUCollider[MaxRigidbodies];
            _particleReadbackCache = new GPUParticle[MaxParticles];
            _collisionPairCache = new CollisionPair[MaxCollisionPairs];
            _contactPointCache = new ContactPoint[MaxContactPoints];
            _gjkSimplexCache = new GJKSimplex[MaxSimplexes];
             _collisionResolutionCache = new CollisionResolutionData[MaxCollisionPairs];
        }

        private void CreateSynchronizationObjects()
        {
            _physicsFence = VkFence.CreateNotSignaled(_context);
            _physicsSemaphore = VkSemaphore.Create(_context);
        }

        private async Task InitializeAllBuffers()
        {
            var batch = _context.ComputeSubmitContext.CreateBatch();

            // Clear all buffers
            await ClearAllBuffersAsync(batch);

            // Submit initialization
            var flushOp = _context.ComputeSubmitContext.FlushSingle(batch, VkFence.CreateNotSignaled(_context));
            await flushOp.WaitAsync();
        }

        private async Task ClearAllBuffersAsync(UploadBatch batch)
        {
            // Clear collision buffers
            var clearCollisionPair = new CollisionPair { BodyA = uint.MaxValue, BodyB = uint.MaxValue };
            var collisionPairClear = new CollisionPair[MaxCollisionPairs];
            Array.Fill(collisionPairClear, clearCollisionPair);
            _collisionPairBuffer.StageData(batch, collisionPairClear);

            // Clear manifold buffer
            var manifoldClear = new Manifold { BodyA = uint.MaxValue, BodyB = uint.MaxValue };
            var manifoldArray = new Manifold[MaxCollisionPairs];
            Array.Fill(manifoldArray, manifoldClear);
            _contactManifoldBuffer.StageData(batch, manifoldArray);

            // Clear contact point buffer
            var contactPointClear = new ContactPoint();
            var contactPointArray = new ContactPoint[MaxContactPoints];
            Array.Fill(contactPointArray, contactPointClear);
            _contactPointBuffer.StageData(batch, contactPointArray);

            // Clear GJK simplex buffer
            var gjkClear = new GJKSimplex();
            var gjkArray = new GJKSimplex[MaxSimplexes];
            Array.Fill(gjkArray, gjkClear);
            _gjkSimplexBuffer.StageData(batch, gjkArray);

            // Clear EPA triangle buffer
            var epaClear = new EPATriangle();
            var epaArray = new EPATriangle[MaxEPATriangles];
            Array.Fill(epaArray, epaClear);
            _epaTriangleBuffer.StageData(batch, epaArray);

            // Clear SAT result buffer
            var satClear = new SATResult();
            var satArray = new SATResult[MaxSATResults];
            Array.Fill(satArray, satClear);
            _satResultBuffer.StageData(batch, satArray);

            // Clear CCD result buffer
            var ccdClear = new CCDResult();
            var ccdArray = new CCDResult[MaxCCDResults];
            Array.Fill(ccdArray, ccdClear);
            _ccdResultBuffer.StageData(batch, ccdArray);

            // Clear counters
            var counterClear = new uint[2] { 0, 0 };
            _collisionCounterBuffer.StageData(batch, counterClear);
            _broadphaseCounterBuffer.StageData(batch, counterClear);

            // Clear broadphase buffers
            var cellClear = new BroadphaseCell { StartIndex = 0, Count = 0, MaxObjects = MaxObjectsPerCell };
            var cellArray = new BroadphaseCell[BroadphaseGridSize];
            Array.Fill(cellArray, cellClear);
            _broadphaseCellBuffer.StageData(batch, cellArray);

            var objectClear = new BroadphaseObject[MaxRigidbodies * 27];
            _broadphaseObjectBuffer.StageData(batch, objectClear);

            // Add barriers
            Span<BufferMemoryBarrier> barriers = stackalloc BufferMemoryBarrier[]
            {
                CreateBufferBarrier(_collisionPairBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_contactManifoldBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_contactPointBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_gjkSimplexBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_epaTriangleBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_satResultBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_ccdResultBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_collisionCounterBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_broadphaseCellBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                CreateBufferBarrier(_broadphaseObjectBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
            };

            batch.PipelineBarrier(PipelineStageFlags.TransferBit, PipelineStageFlags.ComputeShaderBit,
                bufferMemoryBarriers: barriers);
        }

        public async Task UpdatePhysicsAsync(float deltaTime)
        {
            if (!_isInitialized || _rigidbodies.Count == 0) return;

            // Update sleeping bodies
            if (_useSleeping)
            {
                UpdateSleepingBodies(deltaTime);
            }

            // Clear collision events from previous frame
            _collisionEvents.Clear();
            _currentFrameCollisions.Clear();

            // Wait for any pending physics operation
            await WaitForPhysicsCompletionAsync();

            _accumulator += deltaTime;
            int steps = 0;

            // Process up to 4 physics steps per frame
            while (_accumulator >= FIXED_TIMESTEP && steps < 4)
            {
                var stepFence = VkFence.CreateNotSignaled(_context);

                try
                {
                    await StepPhysicsAsync(FIXED_TIMESTEP);

                    // Wait for this step to complete
                    await _context.ComputeSubmitContext.Flush(stepFence).WaitAsync();

                    // Immediately read back results
                    await ReadBackPhysicsDataAsync();

                    // Process collision events
                    ProcessCollisionEvents();

                    // Age persistent manifolds
                    AgePersistentManifolds();

                    // Debug output
                    var activeEvents = GetCollisionEvents();
                    if (activeEvents.Count > 0)
                    {
                        _logger.Debug($"Collision events this step: {activeEvents.Count}");
                    }
                }
                finally
                {
                    stepFence.Dispose();
                }

                _accumulator -= FIXED_TIMESTEP;
                steps++;
            }

            // If there are still physics operations pending, wait for them
            await WaitForPhysicsCompletionAsync();
        }

        private async Task StepPhysicsAsync(float deltaTime)
        {
            // 1. Upload data to GPU
            var uploadBatch = _context.ComputeSubmitContext.CreateBatch();
            await UploadPhysicsDataAsync(uploadBatch);

            // Correct pipeline barrier: Transfer → Compute
            var uploadBarrier = new BufferMemoryBarrier[]
            {
                new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    Buffer = _rigidbodyBuffer.Buffer,
                    Offset = 0,
                    Size = Vk.WholeSize
                },
                new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    Buffer = _colliderBuffer.Buffer,
                    Offset = 0,
                    Size = Vk.WholeSize
                }
            };

            uploadBatch.PipelineBarrier(
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.ComputeShaderBit,
                bufferMemoryBarriers: uploadBarrier
            );

            // 2. Integrate velocities
            await IntegratePhysicsAsync(uploadBatch, deltaTime);

            // Barrier: integration → broadphase
            var integrateBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit,
                Buffer = _rigidbodyBuffer.Buffer,
                Offset = 0,
                Size = Vk.WholeSize
            };

            uploadBatch.PipelineBarrier(
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.ComputeShaderBit,
                bufferMemoryBarriers: new[] { integrateBarrier }
            );

            // 3. Clear collision buffers
            await ClearCollisionBuffersAsync(uploadBatch);

            // Submit the first batch
            var uploadFence = VkFence.CreateNotSignaled(_context);
            uploadBatch.Submit();
            await _context.ComputeSubmitContext.Flush(uploadFence).WaitAsync();
            uploadFence.Dispose();

            // 4. Broadphase
            var broadphaseBatch = _context.ComputeSubmitContext.CreateBatch();

            await RunBroadphaseAsync(broadphaseBatch);
            var broadphaseBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit,
                Buffer = _broadphaseObjectBuffer.Buffer,
                Offset = 0,
                Size = Vk.WholeSize
            };

            broadphaseBatch.PipelineBarrier(
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.ComputeShaderBit,  // NOT HostBit!
                bufferMemoryBarriers: new[] { broadphaseBarrier }
            );

            var broadphaseFence = VkFence.CreateNotSignaled(_context);
            broadphaseBatch.Submit();
            await _context.ComputeSubmitContext.Flush(broadphaseFence).WaitAsync();
            broadphaseFence.Dispose();

            // 5. Narrowphase and contact generation
            await RunNarrowphaseAndContactsAsync(deltaTime);

            // 6. Solve constraints and collisions
            await SolvePhysicsAsync(deltaTime);

            // 7. Update positions and read back results
            await FinalizePhysicsStepAsync(deltaTime);
        }
        private async Task RunNarrowphaseAndContactsAsync(float deltaTime)
        {
            // Detection batch
            var detectionBatch = _context.ComputeSubmitContext.CreateBatch();
            await DetectCollisionsAsync(detectionBatch);

            // Add proper barrier for counter buffer
            var detectionBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                Buffer = _collisionCounterBuffer.Buffer,
                Offset = 0,
                Size = sizeof(uint) * 2
            };

            detectionBatch.PipelineBarrier(
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.TransferBit,
                bufferMemoryBarriers: new[] { detectionBarrier }
            );

            // Copy collision count to staging
            var counterCopy = new BufferCopy
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = sizeof(uint) * 2
            };
            detectionBatch.CopyBuffer(
                _collisionCounterBuffer.Buffer,
                _collisionCounterStagingBuffer,
                counterCopy
            );

            // Barrier for staging buffer - FIXED: Use correct stage flags
            var stagingBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.HostReadBit,
                Buffer = _collisionCounterStagingBuffer,
                Offset = 0,
                Size = sizeof(uint) * 2
            };

            detectionBatch.PipelineBarrier(
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.HostBit,  // Correct stage for host operations
                bufferMemoryBarriers: new[] { stagingBarrier }
            );

            var detectionFence = VkFence.CreateNotSignaled(_context);
            detectionBatch.Submit();
            await _context.ComputeSubmitContext.Flush(detectionFence).WaitAsync();
            detectionFence.Dispose();

            // Read collision count
            uint collisionCount = await ReadCollisionCountAsync();
            _lastCollisionCount = collisionCount;

            if (collisionCount == 0) return;

            // Generate contacts
            var contactBatch = _context.ComputeSubmitContext.CreateBatch();
            await GenerateContactsAsync(contactBatch);

            var contactFence = VkFence.CreateNotSignaled(_context);
            contactBatch.Submit();
            await _context.ComputeSubmitContext.Flush(contactFence).WaitAsync();
            contactFence.Dispose();
        }

        private async Task SolvePhysicsAsync(float deltaTime)
        {
            var solveBatch = _context.ComputeSubmitContext.CreateBatch();

            // Solve constraints if any
            if (_constraints.Count > 0)
            {
                await SolveConstraintsAsync(solveBatch);

                var constraintBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.ShaderWriteBit,
                    DstAccessMask = AccessFlags.ShaderReadBit,
                    Buffer = _rigidbodyBuffer.Buffer,
                    Offset = 0,
                    Size = Vk.WholeSize
                };

                solveBatch.PipelineBarrier(
                    PipelineStageFlags.ComputeShaderBit,
                    PipelineStageFlags.ComputeShaderBit,
                    bufferMemoryBarriers: new[] { constraintBarrier }
                );
            }

            // Solve collisions with multiple iterations
            for (uint iteration = 0; iteration < SOLVER_ITERATIONS; iteration++)
            {
                if (_lastCollisionCount > 0)
                {
                    // Solve contacts
                    await SolveContactsAsync(solveBatch, iteration, _lastCollisionCount);

                    var contactBarrier = new BufferMemoryBarrier
                    {
                        SType = StructureType.BufferMemoryBarrier,
                        SrcAccessMask = AccessFlags.ShaderWriteBit,
                        DstAccessMask = AccessFlags.ShaderReadBit,
                        Buffer = _rigidbodyBuffer.Buffer,
                        Offset = 0,
                        Size = Vk.WholeSize
                    };

                    solveBatch.PipelineBarrier(
                        PipelineStageFlags.ComputeShaderBit,
                        PipelineStageFlags.ComputeShaderBit,
                        bufferMemoryBarriers: new[] { contactBarrier }
                    );

                    // Solve friction (first few iterations)
                    if (iteration < FRICTION_ITERATIONS)
                    {
                        await SolveFrictionAsync(solveBatch, iteration, _lastCollisionCount);

                        var frictionBarrier = new BufferMemoryBarrier
                        {
                            SType = StructureType.BufferMemoryBarrier,
                            SrcAccessMask = AccessFlags.ShaderWriteBit,
                            DstAccessMask = AccessFlags.ShaderReadBit,
                            Buffer = _rigidbodyBuffer.Buffer,
                            Offset = 0,
                            Size = Vk.WholeSize
                        };

                        solveBatch.PipelineBarrier(
                            PipelineStageFlags.ComputeShaderBit,
                            PipelineStageFlags.ComputeShaderBit,
                            bufferMemoryBarriers: new[] { frictionBarrier }
                        );
                    }
                }
            }

            var solveFence = VkFence.CreateNotSignaled(_context);
            solveBatch.Submit();
            await _context.ComputeSubmitContext.Flush(solveFence).WaitAsync();
            solveFence.Dispose();
        }

        private async Task FinalizePhysicsStepAsync(float deltaTime)
        {
            var finalBatch = _context.ComputeSubmitContext.CreateBatch();

            // Update positions
            await UpdatePositionsAsync(finalBatch, deltaTime);

            // Barrier: compute → transfer
            var computeToTransferBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                Buffer = _rigidbodyBuffer.Buffer,
                Offset = 0,
                Size = (uint)(Unsafe.SizeOf<GPURigidbody>() * _rigidbodies.Count)
            };

            finalBatch.PipelineBarrier(
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.TransferBit,
                bufferMemoryBarriers: new[] { computeToTransferBarrier }
            );

            // Copy results to staging
            var copy = new BufferCopy
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = (uint)(Unsafe.SizeOf<GPURigidbody>() * _rigidbodies.Count)
            };

            finalBatch.CopyBuffer(_rigidbodyBuffer.Buffer, _rigidbodyStagingBuffer, copy);

            // Barrier: transfer → host - FIXED: Use correct stage flags
            var transferToHostBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.HostReadBit,
                Buffer = _rigidbodyStagingBuffer,
                Offset = 0,
                Size = (uint)(Unsafe.SizeOf<GPURigidbody>() * _rigidbodies.Count)
            };

            finalBatch.PipelineBarrier(
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.HostBit,  // Correct stage for host operations
                bufferMemoryBarriers: new[] { transferToHostBarrier }
            );

            var finalFence = VkFence.CreateNotSignaled(_context);
            finalBatch.Submit();
            await _context.ComputeSubmitContext.Flush(finalFence).WaitAsync();
            finalFence.Dispose();

            // Read back results
            await ReadBackPhysicsDataAsync();
        }

        private async Task RunNarrowphaseAsync(float deltaTime)
        {
            // First pass: AABB collision detection
            var detectionBatch = _context.ComputeSubmitContext.CreateBatch();
            await DetectCollisionsAsync(detectionBatch);

            // Barrier before reading counter
            var detectionBarrier = CreateBufferBarrier(_collisionCounterBuffer.Buffer,
                AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit | AccessFlags.TransferReadBit);
            detectionBatch.PipelineBarrier(PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit,
                bufferMemoryBarriers: new[] { detectionBarrier });

            // Read collision count
            var counterCopy = new BufferCopy { SrcOffset = 0, DstOffset = 0, Size = sizeof(uint) * 2 };
            detectionBatch.CopyBuffer(_collisionCounterBuffer.Buffer, _collisionCounterStagingBuffer, counterCopy);

            var counterBarrier = CreateBufferBarrier(_collisionCounterStagingBuffer,
                AccessFlags.TransferWriteBit, AccessFlags.HostReadBit);
            detectionBatch.PipelineBarrier(PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit,
                bufferMemoryBarriers: new[] { counterBarrier });
            detectionBatch.Submit();
            // Submit and wait for count
            var countFence = VkFence.CreateNotSignaled(_context);
            await _context.ComputeSubmitContext.Flush(countFence).WaitAsync();
            countFence.Dispose();

            // Read collision count
            uint collisionCount = await ReadCollisionCountAsync();
            _lastCollisionCount = collisionCount;

            if (collisionCount == 0) return;

            // Second pass: GJK for precise collision detection
            var gjkBatch = _context.ComputeSubmitContext.CreateBatch();
            await RunGJKAsync(gjkBatch, collisionCount);
            var gjkFence = VkFence.CreateNotSignaled(_context);
           

            // Barrier before EPA
            var gjkBarrier = CreateBufferBarrier(_gjkSimplexBuffer.Buffer,
                AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit);
            gjkBatch.PipelineBarrier(PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit,
                bufferMemoryBarriers: new[] { gjkBarrier });

            // Third pass: EPA for penetration depth
            await RunEPAAsync(gjkBatch, collisionCount);

            // Barrier before SAT
            var epaBarrier = CreateBufferBarrier(_epaTriangleBuffer.Buffer,
                AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit);
            gjkBatch.PipelineBarrier(PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit,
                bufferMemoryBarriers: new[] { epaBarrier });

            // Fourth pass: SAT for face/edge contacts
            await RunSATAsync(gjkBatch, collisionCount);
            gjkBatch.Submit();
            await _context.ComputeSubmitContext.Flush(gjkFence).WaitAsync();
            gjkFence.Dispose();
        }

        private async Task<uint> ReadCollisionCountAsync()
        {
            uint dataSize = sizeof(uint) * 2;
            uint collisionCount;

            using (var mappedMemory = _collisionCounterStagingBuffer.MapMemory(dataSize, 0))
            {
                var counters = mappedMemory.GetSpan<uint>();
                collisionCount = counters[0];
                uint debugCounter = counters[1];

                if (collisionCount > 0)
                {
                    _logger.Debug($"Found {collisionCount} potential collision pairs");
                }
            }

            return collisionCount;
        }

        private async Task RunGJKAsync(UploadBatch batch, uint collisionCount)
        {
            var material = new MaterialPass(_gjkPipeline);

            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<CollisionPair>(_collisionPairBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<GJKSimplex>(_gjkSimplexBuffer, 3, 0));
            material.BindResource(new StorageBufferBinding<uint>(_collisionCounterBuffer, 4, 0)); // Это AtomicCounter в шейдере


            material.PushConstant("gjk", new GJKConstants
            {
                MaxIterations = MAX_GJK_ITERATIONS,
                Epsilon = GJK_EPSILON,
                MaxSimplexSize = MAX_SIMPLEX_SIZE,
                EnableMinkowski = 1u
            });

            material.CmdPushConstants(batch);
            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_gjkPipeline, PipelineBindPoint.Compute);

            uint groupsX = (collisionCount + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task RunEPAAsync(UploadBatch batch, uint collisionCount)
        {
            var material = new MaterialPass(_epaPipeline);

            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<CollisionPair>(_collisionPairBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<GJKSimplex>(_gjkSimplexBuffer, 3, 0));
            material.BindResource(new StorageBufferBinding<EPATriangle>(_epaTriangleBuffer, 4, 0));
            material.BindResource(new StorageBufferBinding<uint>(_collisionCounterBuffer, 5, 0));
            material.BindResource(new StorageBufferBinding<Manifold>(_contactManifoldBuffer, 6, 0));

            material.PushConstant("epa", new EPAConstants
            {
                MaxIterations = MAX_EPA_ITERATIONS,
                Epsilon = EPA_EPSILON,
                MaxTriangles = MAX_EPA_TRIANGLES,
                DepthTolerance = DEPTH_TOLERANCE
            });

            material.CmdPushConstants(batch);
            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_epaPipeline, PipelineBindPoint.Compute);

            uint groupsX = (collisionCount + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task RunSATAsync(UploadBatch batch, uint collisionCount)
        {
            var material = new MaterialPass(_satPipeline);

            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<CollisionPair>(_collisionPairBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<SATResult>(_satResultBuffer, 3, 0));
            material.BindResource(new UniformBufferBinding(_physicsParamsBuffer, 0, 1));

            material.PushConstant("sat", new SATConstants
            {
                MaxFaceChecks = MAX_FACE_CHECKS,
                MaxEdgeChecks = MAX_EDGE_CHECKS,
                Epsilon = SAT_EPSILON,
                FeatureSwapThreshold = FEATURE_SWAP_THRESHOLD
            });

            material.CmdPushConstants(batch);
            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_satPipeline, PipelineBindPoint.Compute);

            uint groupsX = (collisionCount + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task GenerateContactsAsync(UploadBatch batch)
        {
            var material = new MaterialPass(_contactGenerationPipeline);

            material.BindResource(new StorageBufferBinding<CollisionPair>(_collisionPairBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<Manifold>(_contactManifoldBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<ContactPoint>(_contactPointBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<EPATriangle>(_epaTriangleBuffer, 3, 0));
            material.BindResource(new StorageBufferBinding<SATResult>(_satResultBuffer, 4, 0));
            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 5, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 6, 0));

            //material.BindResource(new StorageBufferBinding<PersistentContact>(_persistentContactBuffer, 5, 0));
            material.BindResource(new UniformBufferBinding(_physicsParamsBuffer, 0, 1));

            material.PushConstant("cg", new ContactGenerationConstants
            {
                MaxContactsPerPair = MAX_CONTACT_POINTS_PER_PAIR,
                ContactMergeDistance = CONTACT_MERGE_DISTANCE,
                PersistentThreshold = PERSISTENT_THRESHOLD,
                NormalTolerance = NORMAL_TOLERANCE,
                EnableWarmStarting = 1u
            });
            batch.BindPipeline(_contactGenerationPipeline, PipelineBindPoint.Compute);

            material.CmdPushConstants(batch);
            _bindingManager.BindResourcesForMaterial(0, material, batch, true);

            uint groupsX = (_lastCollisionCount + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }



        private async Task SolveContactsAsync(UploadBatch batch, uint iteration, uint collisionCount)
        {
            var material = new MaterialPass(_collisionResponsePipeline);

            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<Manifold>(_contactManifoldBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<ContactPoint>(_contactPointBuffer, 3, 0));

            // Use a simple uniform buffer for parameters
            var solverParams = new ContactSolverParams
            {
                DeltaTime = FIXED_TIMESTEP,
                Iteration = iteration,
                TotalIterations = SOLVER_ITERATIONS,
                BaumgarteFactor = BAUMGARTE_FACTOR,
                BaumgarteSlop = BAUMGARTE_SLOP,
                MaxCorrection = MAX_CORRECTION,
                WarmStartFactor = (iteration == 0) ? WARM_START_FACTOR : 0.0f,
                RelaxationFactor = RELAXATION_FACTOR
            };

            // Create uniform buffer if not exists
            if (_solverParamsBuffer == null)
            {
                _solverParamsBuffer = new UniformBuffer(_context, (uint)Unsafe.SizeOf<ContactSolverParams>());
            }

            await _solverParamsBuffer.UpdateAsync(solverParams);
            material.BindResource(new UniformBufferBinding(_solverParamsBuffer, 0, 1));

            material.CmdPushConstants(batch);
            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_collisionResponsePipeline, PipelineBindPoint.Compute);

            uint groupsX = (collisionCount + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }



        private async Task SolveFrictionAsync(UploadBatch batch, uint iteration, uint collisionCount)
        {
            var material = new MaterialPass(_frictionSolverPipeline);

            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<Manifold>(_contactManifoldBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<ContactPoint>(_contactPointBuffer, 3, 0));
            material.BindResource(new UniformBufferBinding(_physicsParamsBuffer, 0, 1));

            material.PushConstant("friction", new FrictionSolverConstants
            {
                DeltaTime = FIXED_TIMESTEP,
                Iteration = iteration,
                FrictionIterations = FRICTION_ITERATIONS,
                StaticFrictionThreshold = STATIC_FRICTION_THRESHOLD,
                DynamicFrictionScale = DYNAMIC_FRICTION_SCALE,
                FrictionStiffness = FRICTION_STIFFNESS,
                CollisionCount = collisionCount  // Pass collision count
            });

            material.CmdPushConstants(batch);
            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_frictionSolverPipeline, PipelineBindPoint.Compute);

            uint groupsX = (collisionCount + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task RunCCDAsync(UploadBatch batch)
        {
            var material = new MaterialPass(_ccdPipeline);

            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<CollisionPair>(_collisionPairBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<CCDResult>(_ccdResultBuffer, 3, 0));
            material.BindResource(new StorageBufferBinding<uint>(_collisionCounterBuffer, 4, 0));

            material.PushConstant("ccd", new CCDConstants
            {
                DeltaTime = FIXED_TIMESTEP,
                MaxSteps = CCD_MAX_STEPS,
                Tolerance = 0.001f,
                VelocityThreshold = CCD_THRESHOLD,
                EnableLinearCCD = 1u,
                EnableAngularCCD = 1u
            });

            material.CmdPushConstants(batch);
            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_ccdPipeline, PipelineBindPoint.Compute);

            uint groupsX = ((uint)_rigidbodies.Count + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task UploadPhysicsDataAsync(UploadBatch batch)
        {
            if (_rigidbodies.Count == 0) return;

            var physicsParams = new PhysicsParams
            {
                Gravity = new Vector4(Gravity.X, Gravity.Y, Gravity.Z, 0),
                DeltaTime = FIXED_TIMESTEP,
                ParticleCount = 0,
                BodyCount = (uint)_rigidbodies.Count,
                ColliderCount = (uint)_colliders.Count,
                ConstraintCount = (uint)_constraints.Count,
                MaxCollisionPairs = MaxCollisionPairs,
                RestitutionScale = 0.5f,
                ContactBias = 0.2f,
                ContactSlop = 0.01f,
                Padding = Vector2.Zero
            };

            await _physicsParamsBuffer.UpdateAsync(physicsParams);

            // Upload rigidbody data
            var rigidbodyData = new GPURigidbody[_rigidbodies.Count];
            for (int i = 0; i < _rigidbodies.Count; i++)
            {
                var rb = _rigidbodies[i];
                var transform = rb.Entity.Transform;

                var position = transform.Position;
                var rotation = Quaternion.Normalize(transform.Rotation);

                Matrix4x4.Decompose(transform.WorldMatrix, out Vector3 scale, out _, out _);

                var collider = _colliders.FirstOrDefault(c => c.Entity?.GetComponent<Rigidbody>() == rb);
                var (inertiaTensor, inverseInertiaTensor) = CalculateInertiaTensor(rb, collider, scale);

                rigidbodyData[i] = new GPURigidbody
                {
                    Position = position,
                    Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
                    Velocity = rb.Velocity,
                    AngularVelocity = rb.AngularVelocity,
                    Force = rb.Force,
                    Torque = rb.Torque,
                    Mass = Math.Max(0.001f, rb.Mass),
                    InverseMass = rb.Mass > 0.0001f ? 1.0f / rb.Mass : 0.0f,
                    Restitution = Math.Clamp(rb.Restitution, 0.0f, 1.0f),
                    Friction = Math.Max(0.0f, rb.Friction),
                    BodyType = (uint)rb.BodyType,
                    ColliderIndex = GetColliderIndex(collider),
                    IsActive = rb.IsActive ? 1u : 0u,
                    InertiaTensor = inertiaTensor,
                    InverseInertiaTensor = inverseInertiaTensor,
                    CenterOfMass = collider?.Offset ?? Vector3.Zero,
                    LinearDamping = rb.LinearDamping,
                    AngularDamping = rb.AngularDamping
                };

            }

            // Upload collider data
            var colliderData = new GPUCollider[_colliders.Count];
            for (int i = 0; i < _colliders.Count; i++)
            {
                var col = _colliders[i];
                var transform = col.Entity.Transform;
                var rigidbody = col.Entity.GetComponent<Rigidbody>();

                // IMPORTANT: col.Size is already half-extents, don't multiply by 0.5f
                Vector3 halfExtents = col.Size * transform.LocalScale;  // Scale the half-extents

                colliderData[i] = new GPUCollider
                {
                    Position = transform.Position + col.Offset,
                    Rotation = new Vector4(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W),
                    Size = halfExtents,  // This should be HALF-EXTENTS
                    Radius = Math.Max(0.001f, col.Radius * Math.Max(transform.LocalScale.X, Math.Max(transform.LocalScale.Y, transform.LocalScale.Z))),
                    Height = Math.Max(0.001f, col.Height * transform.LocalScale.Y),
                    Shape = (uint)col.Shape,
                    IsTrigger = col.IsTrigger ? 1u : 0u,
                    RigidbodyIndex = rigidbody != null ? GetRigidbodyIndex(rigidbody) : uint.MaxValue,
                    WorldMatrix = transform.WorldMatrix,
                };

            }

            // Stage data
            _rigidbodyBuffer.StageData(batch, rigidbodyData);
            _colliderBuffer.StageData(batch, colliderData);

            // If constraints changed, upload them
            if (_constraintsDirty || _constraints.Count != _lastConstraintCount)
            {
                var constraintData = new GPUConstraint[_constraints.Count];
                for (int i = 0; i < _constraints.Count; i++)
                {
                    var constraint = _constraints[i];
                    constraintData[i] = new GPUConstraint
                    {
                        BodyA = GetRigidbodyIndex(constraint.BodyA),
                        BodyB = GetRigidbodyIndex(constraint.BodyB),
                        LocalAnchorA = constraint.AnchorA,
                        LocalAnchorB = constraint.AnchorB,
                        MinDistance = constraint.MinDistance,
                        MaxDistance = constraint.MaxDistance,
                        Stiffness = constraint.Stiffness,
                        Damping = constraint.Damping,
                        ConstraintType = (uint)constraint.Type,
                    };
                }
                _constraintBuffer.StageData(batch, constraintData);
                _lastConstraintCount = _constraints.Count;
                _constraintsDirty = false;
            }
        }

        private async Task ClearCollisionBuffersAsync(UploadBatch batch)
        {
            // Clear collision pair buffer
            var clearValue = new CollisionPair { BodyA = uint.MaxValue, BodyB = uint.MaxValue };
            var clearArray = new CollisionPair[MaxCollisionPairs];
            Array.Fill(clearArray, clearValue);

            // Clear manifold buffer
            var manifoldClear = new Manifold { BodyA = uint.MaxValue, BodyB = uint.MaxValue };
            var manifoldArray = new Manifold[MaxCollisionPairs];
            Array.Fill(manifoldArray, manifoldClear);

            // Clear contact point buffer
            var contactClear = new ContactPoint();
            var contactArray = new ContactPoint[MaxContactPoints];
            Array.Fill(contactArray, contactClear);

            // Clear counters
            var counterClear = new uint[2] { 0, 0 };

            // Stage data
            _collisionPairBuffer.StageData(batch, clearArray);
            _contactManifoldBuffer.StageData(batch, manifoldArray);
            _contactPointBuffer.StageData(batch, contactArray);
            _collisionCounterBuffer.StageData(batch, counterClear);

            // Barriers
            var clearBarriers = new BufferMemoryBarrier[]
            {
                CreateBufferBarrier(_collisionPairBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit),
                CreateBufferBarrier(_contactManifoldBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit),
                CreateBufferBarrier(_contactPointBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit),
                CreateBufferBarrier(_collisionCounterBuffer.Buffer, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit)
            };

            batch.PipelineBarrier(PipelineStageFlags.TransferBit, PipelineStageFlags.ComputeShaderBit,
                bufferMemoryBarriers: clearBarriers);
        }

        private async Task RunBroadphaseAsync(UploadBatch batch)
        {
            var material = new MaterialPass(_broadphasePipeline);
            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<BroadphaseObject>(_broadphaseObjectBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<BroadphaseCell>(_broadphaseCellBuffer, 3, 0));
            material.BindResource(new StorageBufferBinding<uint>(_broadphaseCounterBuffer, 4, 0));

            material.PushConstant("bpc", new BroadphasePushConstants
            {
                CellSize = BroadphaseCellSize,
                GridSize = BroadphaseGridSize,
                MaxObjectsPerCell = MaxObjectsPerCell,
                BodyCount = (uint)_rigidbodies.Count,
                ColliderCount = (uint)_colliders.Count
            });
            material.CmdPushConstants(batch);

            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_broadphasePipeline, PipelineBindPoint.Compute);

            uint groupsX = ((uint)_rigidbodies.Count + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task DetectCollisionsAsync(UploadBatch batch)
        {
            var material = new MaterialPass(_collisionDetectionPipeline);

            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUCollider>(_colliderBuffer, 1, 0));
            material.BindResource(new StorageBufferBinding<CollisionPair>(_collisionPairBuffer, 2, 0));
            material.BindResource(new StorageBufferBinding<Manifold>(_contactManifoldBuffer, 3, 0));
            material.BindResource(new StorageBufferBinding<uint>(_collisionCounterBuffer, 4, 0));
            material.BindResource(new UniformBufferBinding(_physicsParamsBuffer, 0, 1));

            material.PushConstant("pc", new PhysicsPushConstants
            {
                DeltaTime = FIXED_TIMESTEP,
                Gravity = 9.81f,
                ParticleCount = 0,
                BodyCount = (uint)_rigidbodies.Count,
                ColliderCount = (uint)_colliders.Count,
                ConstraintCount = (uint)_constraints.Count
            });

            batch.BindPipeline(_collisionDetectionPipeline, PipelineBindPoint.Compute);
            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            material.CmdPushConstants(batch);

            uint groupsX = ((uint)_rigidbodies.Count + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task IntegratePhysicsAsync(UploadBatch batch, float deltaTime)
        {
            var material = new MaterialPass(_physicsIntegratePipeline);
            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new UniformBufferBinding(_physicsParamsBuffer, 0, 1));

            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_physicsIntegratePipeline, PipelineBindPoint.Compute);

            uint groupsX = ((uint)_rigidbodies.Count + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task SolveConstraintsAsync(UploadBatch batch)
        {
            if (_constraints.Count == 0) return;

            var material = new MaterialPass(_constraintSolverPipeline);
            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new StorageBufferBinding<GPUConstraint>(_constraintBuffer, 3, 0));
            material.BindResource(new UniformBufferBinding(_physicsParamsBuffer, 0, 1));

            material.PushConstant("pc", new PhysicsPushConstants
            {
                DeltaTime = FIXED_TIMESTEP,
                Gravity = 9.81f,
                ParticleCount = 0,
                BodyCount = (uint)_rigidbodies.Count,
                ColliderCount = (uint)_colliders.Count,
                ConstraintCount = (uint)_constraints.Count
            });
            material.CmdPushConstants(batch);

            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_constraintSolverPipeline, PipelineBindPoint.Compute);

            uint groupsX = ((uint)_constraints.Count + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task UpdatePositionsAsync(UploadBatch batch, float deltaTime)
        {
            var material = new MaterialPass(_updatePositionsPipeline);
            material.BindResource(new StorageBufferBinding<GPURigidbody>(_rigidbodyBuffer, 0, 0));
            material.BindResource(new UniformBufferBinding(_physicsParamsBuffer, 0, 1));

            material.CmdPushConstants(batch);

            _bindingManager.BindResourcesForMaterial(0, material, batch, true);
            batch.BindPipeline(_updatePositionsPipeline, PipelineBindPoint.Compute);

            uint groupsX = ((uint)_rigidbodies.Count + 255) / 256;
            batch.Dispatch(groupsX, 1, 1);
        }

        private async Task CopyResultsToStagingAsync(UploadBatch batch)
        {
            if (_rigidbodies.Count == 0) return;

            uint rigidbodyDataSize = (uint)(Unsafe.SizeOf<GPURigidbody>() * _rigidbodies.Count);

            // Barrier: ensure compute shaders have finished
            var computeToTransferBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                Buffer = _rigidbodyBuffer.Buffer,
                Offset = 0,
                Size = rigidbodyDataSize
            };

            batch.PipelineBarrier(PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit,
                bufferMemoryBarriers: new[] { computeToTransferBarrier });

            // Copy to staging buffer
            var copy = new BufferCopy
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = rigidbodyDataSize
            };

            batch.CopyBuffer(_rigidbodyBuffer.Buffer, _rigidbodyStagingBuffer, copy);

            // Barrier: ensure transfer is complete before host reads
            var transferToHostBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.HostReadBit,
                Buffer = _rigidbodyStagingBuffer,
                Offset = 0,
                Size = rigidbodyDataSize
            };

            batch.PipelineBarrier(PipelineStageFlags.TransferBit, PipelineStageFlags.HostBit,
                bufferMemoryBarriers: new[] { transferToHostBarrier });
        }

        private async Task ReadBackPhysicsDataAsync()
        {
            if (_rigidbodies.Count == 0) return;

            var rigidbodyData = ReadRigidbodyData();

            // Validate and update transforms
            for (int i = 0; i < rigidbodyData.Length; i++)
            {
                var data = rigidbodyData[i];

                // Check for NaN
                if (float.IsNaN(data.Position.X) || float.IsNaN(data.Position.Y) || float.IsNaN(data.Position.Z) ||
                    float.IsNaN(data.Rotation.X) || float.IsNaN(data.Rotation.Y) ||
                    float.IsNaN(data.Rotation.Z) || float.IsNaN(data.Rotation.W))
                {
                    _logger.Error($"Invalid physics data in rigidbody {i}. Skipping update.");
                    continue;
                }

                UpdateRigidbodyTransform(_rigidbodies[i], data);
            }
        }

        private unsafe GPURigidbody[] ReadRigidbodyData()
        {
            if (_rigidbodies.Count == 0) return Array.Empty<GPURigidbody>();

            uint dataSize = (uint)(Unsafe.SizeOf<GPURigidbody>() * _rigidbodies.Count);

            using var mappedMemory = _rigidbodyStagingBuffer.MapMemory(dataSize, 0);
            var rigidbodiesData = mappedMemory.GetSpan<GPURigidbody>();

            var result = new GPURigidbody[_rigidbodies.Count];

            for (int i = 0; i < _rigidbodies.Count; i++)
            {
                var data = rigidbodiesData[i];

                // Validate data
                if (float.IsNaN(data.Position.X) || float.IsNaN(data.Position.Y) || float.IsNaN(data.Position.Z))
                {
                    _logger.Warn($"Rigidbody {i} has NaN position. Resetting.");
                    data.Position = Vector3.Zero;
                }

                if (float.IsNaN(data.Rotation.X) || float.IsNaN(data.Rotation.Y) ||
                    float.IsNaN(data.Rotation.Z) || float.IsNaN(data.Rotation.W))
                {
                    _logger.Warn($"Rigidbody {i} has NaN rotation. Resetting to identity.");
                    data.Rotation = new Vector4(0, 0, 0, 1);
                }

                // Ensure quaternion is normalized
                float lengthSq = data.Rotation.X * data.Rotation.X +
                                 data.Rotation.Y * data.Rotation.Y +
                                 data.Rotation.Z * data.Rotation.Z +
                                 data.Rotation.W * data.Rotation.W;

                if (lengthSq < 0.5f || lengthSq > 2.0f)
                {
                    float length = (float)Math.Sqrt(lengthSq);
                    if (length > 1e-8f)
                    {
                        data.Rotation = new Vector4(
                            data.Rotation.X / length,
                            data.Rotation.Y / length,
                            data.Rotation.Z / length,
                            data.Rotation.W / length
                        );
                    }
                    else
                    {
                        data.Rotation = new Vector4(0, 0, 0, 1);
                    }
                }

                result[i] = data;
                _rigidbodyReadbackCache[i] = data;
            }

            return result;
        }

        private void UpdateRigidbodyTransform(Rigidbody rb, in GPURigidbody data)
        {
            var entity = rb.Entity;
            if (entity == null) return;

            var transform = entity.Transform;

            // Extract position and rotation
            var position = data.Position;
            var rotation = new Quaternion(data.Rotation.X, data.Rotation.Y, data.Rotation.Z, data.Rotation.W);

            // Check for NaN/Infinity
            bool positionValid = !float.IsNaN(position.X) && !float.IsNaN(position.Y) && !float.IsNaN(position.Z) &&
                                 !float.IsInfinity(position.X) && !float.IsInfinity(position.Y) && !float.IsInfinity(position.Z);

            bool rotationValid = !float.IsNaN(rotation.X) && !float.IsNaN(rotation.Y) && !float.IsNaN(rotation.Z) && !float.IsNaN(rotation.W) &&
                                 !float.IsInfinity(rotation.X) && !float.IsInfinity(rotation.Y) && !float.IsInfinity(rotation.Z) && !float.IsInfinity(rotation.W);

            if (!positionValid || !rotationValid)
            {
                _logger.Error($"Invalid physics data for {rb.Entity.Name}: Pos={position}, Rot={rotation}");
                return;
            }

            // Normalize rotation
            rotation = Quaternion.Normalize(rotation);

            // Ensure valid quaternion
            if (rotation.LengthSquared() < 0.5f)
            {
                rotation = Quaternion.Identity;
            }

            // Update transform
            transform.Position = position;
            transform.Rotation = rotation;

            // Update velocity with validation
            var velocity = data.Velocity;
            var angularVelocity = data.AngularVelocity;

            if (float.IsNaN(velocity.X) || float.IsNaN(velocity.Y) || float.IsNaN(velocity.Z) ||
                float.IsInfinity(velocity.X) || float.IsInfinity(velocity.Y) || float.IsInfinity(velocity.Z))
            {
                velocity = Vector3.Zero;
            }

            if (float.IsNaN(angularVelocity.X) || float.IsNaN(angularVelocity.Y) || float.IsNaN(angularVelocity.Z) ||
                float.IsInfinity(angularVelocity.X) || float.IsInfinity(angularVelocity.Y) || float.IsInfinity(angularVelocity.Z))
            {
                angularVelocity = Vector3.Zero;
            }

            // Clamp velocities
            if (velocity.LengthSquared() > 10000f) velocity = Vector3.Normalize(velocity) * 100f;
            if (angularVelocity.LengthSquared() > 100f) angularVelocity = Vector3.Normalize(angularVelocity) * 10f;

            rb.Velocity = velocity;
            rb.AngularVelocity = angularVelocity;
        }

        private void ProcessCollisionEvents()
        {
            // Process current frame collisions
            for (int i = 0; i < _collisionPairCache.Length; i++)
            {
                var pair = _collisionPairCache[i];
                if (pair.BodyA != uint.MaxValue && pair.BodyB != uint.MaxValue && pair.Depth > 0.001f)
                {
                    var key = (Math.Min(pair.BodyA, pair.BodyB), Math.Max(pair.BodyA, pair.BodyB));

                    if (!_currentFrameCollisions.Add(key))
                        continue;

                    bool wasCollidingLastFrame = _previousFrameCollisions.Contains(key);

                    var collisionEvent = new CollisionEvent
                    {
                        BodyA = pair.BodyA,
                        BodyB = pair.BodyB,
                        ColliderA = pair.ColliderA,
                        ColliderB = pair.ColliderB,
                        Normal = pair.Normal,
                        Depth = pair.Depth,
                        ContactPoint = pair.ContactPoint,
                        Impulse = pair.Impulse,
                        Type = wasCollidingLastFrame ? CollisionEventType.Stay : CollisionEventType.Enter
                    };

                    _collisionEvents.Add(collisionEvent);

                    // Trigger event callbacks
                    if (collisionEvent.Type == CollisionEventType.Enter)
                        OnCollisionEnter?.Invoke(collisionEvent);
                    else if (collisionEvent.Type == CollisionEventType.Stay)
                        OnCollisionStay?.Invoke(collisionEvent);

                    // Debug logging for static-dynamic collisions
                    if (collisionEvent.BodyA < _rigidbodies.Count && collisionEvent.BodyB < _rigidbodies.Count)
                    {
                        var rbA = _rigidbodies[(int)collisionEvent.BodyA];
                        var rbB = _rigidbodies[(int)collisionEvent.BodyB];

                        if ((rbA.BodyType == PhysicsBodyType.Static && rbB.BodyType == PhysicsBodyType.Dynamic) ||
                            (rbB.BodyType == PhysicsBodyType.Static && rbA.BodyType == PhysicsBodyType.Dynamic))
                        {
                            _logger.Trace($"Static-Dynamic collision: BodyA={collisionEvent.BodyA} ({rbA.BodyType}), BodyB={collisionEvent.BodyB} ({rbB.BodyType}), Depth={collisionEvent.Depth:F4}, Impulse={collisionEvent.Impulse:F4}");
                        }
                    }
                }
            }

            // Check for exit events
            var exitsToRemove = new List<(uint, uint)>();
            foreach (var key in _previousFrameCollisions)
            {
                if (!_currentFrameCollisions.Contains(key))
                {
                    var exitEvent = new CollisionEvent
                    {
                        BodyA = key.Item1,
                        BodyB = key.Item2,
                        Type = CollisionEventType.Exit
                    };

                    _collisionEvents.Add(exitEvent);
                    OnCollisionExit?.Invoke(exitEvent);
                    exitsToRemove.Add(key);
                }
            }

            // Remove exit events from previous frame tracking
            foreach (var key in exitsToRemove)
            {
                _previousFrameCollisions.Remove(key);
            }

            // Swap for next frame
            _previousFrameCollisions = new HashSet<(uint, uint)>(_currentFrameCollisions);
        }

        private void AgePersistentManifolds()
        {
            _persistentManifoldAge++;

            // Remove old manifolds
            if (_persistentManifoldAge >= MAX_PERSISTENT_AGE)
            {
                var toRemove = new List<(uint, uint)>();
                foreach (var kvp in _persistentManifolds)
                {
                    if (kvp.Value.Age >= MAX_PERSISTENT_AGE)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in toRemove)
                {
                    _persistentManifolds.Remove(key);
                }

                _persistentManifoldAge = 0;
            }
        }

        private void UpdateSleepingBodies(float deltaTime)
        {
            for (int i = 0; i < _rigidbodies.Count; i++)
            {
                var rb = _rigidbodies[i];
                if (rb.BodyType != PhysicsBodyType.Dynamic) continue;

                float energy = rb.Velocity.LengthSquared() + rb.AngularVelocity.LengthSquared();

                if (energy < _sleepThreshold)
                {
                    if (!_sleepTimers.ContainsKey((uint)i))
                        _sleepTimers[(uint)i] = 0f;

                    _sleepTimers[(uint)i] += deltaTime;

                    if (_sleepTimers[(uint)i] > _sleepTimeThreshold)
                    {
                        rb.State = RigidBodyState.Active;
                        _logger.Debug($"Body {i} is sleeping");
                    }
                }
                else
                {
                    _sleepTimers[(uint)i] = 0f;
                    rb.State = RigidBodyState.Active;
                }
            }
        }

        private (Matrix4x4 Inertia, Matrix4x4 InverseInertia) CalculateInertiaTensor(Rigidbody rb, Collider collider, Vector3 scale)
        {
            if (rb.BodyType == PhysicsBodyType.Static)
            {
                // Static bodies have infinite mass (zero inverse mass)
                return (Matrix4x4.Identity, new Matrix4x4());
            }

            if (collider == null || rb.Mass <= 0)
            {
                // Default inertia for point mass
                float defaultInertia = 1.0f;
                return (Matrix4x4.Identity * defaultInertia, Matrix4x4.Identity * (1.0f / defaultInertia));
            }

            Matrix4x4 inertia = Matrix4x4.Identity;
            float mass = rb.Mass;

            switch (collider.Shape)
            {
                case ColliderShape.Box:
                    Vector3 size = collider.Size * scale;
                    float x2 = size.X * size.X;
                    float y2 = size.Y * size.Y;
                    float z2 = size.Z * size.Z;

                    float ix = mass * (y2 + z2) / 12.0f;
                    float iy = mass * (x2 + z2) / 12.0f;
                    float iz = mass * (x2 + y2) / 12.0f;

                    inertia = new Matrix4x4(
                        ix, 0, 0, 0,
                        0, iy, 0, 0,
                        0, 0, iz, 0,
                        0, 0, 0, 1
                    );
                    break;

                case ColliderShape.Sphere:
                    float radius = collider.Radius * Math.Max(scale.X, Math.Max(scale.Y, scale.Z));
                    float i = 0.4f * mass * radius * radius;

                    inertia = new Matrix4x4(
                        i, 0, 0, 0,
                        0, i, 0, 0,
                        0, 0, i, 0,
                        0, 0, 0, 1
                    );
                    break;

                case ColliderShape.Cylinder:
                    float radiusCyl = collider.Radius * Math.Max(scale.X, scale.Z);
                    float heightCyl = collider.Height * scale.Y;

                    float ixz = mass * (3.0f * radiusCyl * radiusCyl + heightCyl * heightCyl) / 12.0f;
                    float iyCyl = mass * radiusCyl * radiusCyl * 0.5f;

                    inertia = new Matrix4x4(
                        ixz, 0, 0, 0,
                        0, iyCyl, 0, 0,
                        0, 0, ixz, 0,
                        0, 0, 0, 1
                    );
                    break;

                default:
                    // Default to sphere approximation
                    float defaultRadius = 0.5f * Math.Max(scale.X, Math.Max(scale.Y, scale.Z));
                    float defaultI = 0.4f * mass * defaultRadius * defaultRadius;
                    inertia = Matrix4x4.Identity * defaultI;
                    break;
            }

            // Calculate inverse with safety check
            Matrix4x4 inverseInertia;
            if (Matrix4x4.Invert(inertia, out inverseInertia))
            {
                // For kinematic bodies, scale down inverse inertia
                if (rb.BodyType == PhysicsBodyType.Kinematic)
                {
                    inverseInertia *= 0.1f;
                }
            }
            else
            {
                inverseInertia = new Matrix4x4();
            }

            return (inertia, inverseInertia);
        }


        private BufferMemoryBarrier CreateBufferBarrier(VkBuffer buffer, AccessFlags srcAccess, AccessFlags dstAccess)
        {
            return new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = srcAccess,
                DstAccessMask = dstAccess,
                Buffer = buffer,
                Offset = 0,
                Size = Vk.WholeSize
            };
        }

        private async Task WaitForPhysicsCompletionAsync()
        {
            if (_flushOp != null)
            {
                await _flushOp.WaitAsync();
                _flushOp = null;
            }
            _physicsFence.Reset();
        }

        // Public API methods
        public void RegisterRigidbody(Rigidbody rigidbody)
        {
            if (!_rigidbodies.Contains(rigidbody))
            {
                if (_rigidbodies.Count >= MaxRigidbodies)
                {
                    _logger.Warn($"Cannot register rigidbody: maximum capacity ({MaxRigidbodies}) reached");
                    return;
                }
                _rigidbodies.Add(rigidbody);

                // Make sure rigidbody has a collider
                var collider = rigidbody.Entity.GetComponent<Collider>();
                if (collider != null && !_colliders.Contains(collider))
                {
                    _colliders.Add(collider);
                }
            }
        }

        public void UnregisterRigidbody(Rigidbody rigidbody)
        {
            _rigidbodies.Remove(rigidbody);
        }

        public void RegisterCollider(Collider collider)
        {
            if (!_colliders.Contains(collider))
            {
                _colliders.Add(collider);
            }
        }

        public void UnregisterCollider(Collider collider)
        {
            _colliders.Remove(collider);
        }

        public void RegisterConstraint(Constraint constraint)
        {
            if (!_constraints.Contains(constraint))
            {
                constraint.ConstraintIndex = _nextConstraintIndex++;
                _constraints.Add(constraint);
                _constraintsDirty = true;
            }
        }

        public void UnregisterConstraint(Constraint constraint)
        {
            if (_constraints.Remove(constraint))
            {
                _constraintsDirty = true;
            }
        }

        public void RegisterParticleSystem(ParticleSystem system)
        {
            if (!_particleSystems.Contains(system))
            {
                _particleSystems.Add(system);
                system.ParticleBufferId = (uint)_particleSystems.Count - 1;
            }
        }

        public void UnregisterParticleSystem(ParticleSystem system)
        {
            _particleSystems.Remove(system);
        }

        public async Task EmitParticlesAsync(ParticleSystem system, uint count)
        {
            var request = new ParticleEmissionRequest
            {
                ParticleSystemId = system.ParticleBufferId,
                Count = Math.Min(count, MaxParticles - (uint)_particleSystems.Sum(p => p.MaxParticles)),
                Position = new Vector4(system.Entity.Transform.WorldPosition, 0),
                BaseVelocity = new Vector4(system.StartVelocity, 0)
            };
            _emissionQueue.Enqueue(request);
        }

        public uint GetRigidbodyIndex(Rigidbody rigidbody)
        {
            for (int i = 0; i < _rigidbodies.Count; i++)
            {
                if (_rigidbodies[i] == rigidbody)
                {
                    return (uint)i;
                }
            }
            return uint.MaxValue;
        }

        public uint GetColliderIndex(Collider collider)
        {
            if (collider == null) return uint.MaxValue;

            for (int i = 0; i < _colliders.Count; i++)
            {
                if (_colliders[i] == collider)
                    return (uint)i;
            }
            return uint.MaxValue;
        }

        public uint GetConstraintIndex(Constraint constraint)
        {
            return constraint.ConstraintIndex;
        }

        public List<CollisionEvent> GetCollisionEvents()
        {
            return new List<CollisionEvent>(_collisionEvents);
        }

        public List<CollisionPair> GetActiveCollisions()
        {
            var activeCollisions = new List<CollisionPair>();
            for (int i = 0; i < _collisionPairCache.Length; i++)
            {
                if (_collisionPairCache[i].BodyA != uint.MaxValue && _collisionPairCache[i].BodyB != uint.MaxValue)
                {
                    activeCollisions.Add(_collisionPairCache[i]);
                }
            }
            return activeCollisions;
        }

        public void Dispose()
        {
            _flushOp?.WaitAsync().Wait();

            // Dispose buffers
            _rigidbodyBuffer?.Dispose();
            _colliderBuffer?.Dispose();
            _particleBuffer?.Dispose();
            _constraintBuffer?.Dispose();
            _collisionPairBuffer?.Dispose();
            _collisionCounterBuffer?.Dispose();
            _contactManifoldBuffer?.Dispose();
            _broadphaseCellBuffer?.Dispose();
            _broadphaseObjectBuffer?.Dispose();
            _broadphaseCounterBuffer?.Dispose();
            _physicsParamsBuffer?.Dispose();
            _gjkSimplexBuffer?.Dispose();
            _epaTriangleBuffer?.Dispose();
            _satResultBuffer?.Dispose();
            _ccdResultBuffer?.Dispose();
            _contactPointBuffer?.Dispose();
            _persistentContactBuffer?.Dispose();

            // Dispose staging buffers
            _rigidbodyStagingBuffer?.Dispose();
            _particleStagingBuffer?.Dispose();
            _colliderStagingBuffer?.Dispose();
            _collisionPairStagingBuffer?.Dispose();
            _contactPointStagingBuffer?.Dispose();
            _gjkStagingBuffer?.Dispose();
            _collisionCounterStagingBuffer?.Dispose();

            // Dispose synchronization objects
            _physicsFence?.Dispose();
            _physicsSemaphore?.Dispose();
        }

        // Internal helper classes
        private class PersistentManifold
        {
            public List<PersistentContact> Contacts { get; } = new();
            public Vector3 Normal { get; set; }
            public int Age { get; set; }

            public PersistentManifold(in Manifold manifold)
            {
                Normal = manifold.Normal;
                Age = 0;
            }

            public void Merge(in Manifold newManifold, float threshold)
            {
                Age = 0;
            }
        }

        private struct ContactWarmStartData
        {
            public float NormalImpulse;
            public float FrictionImpulse1;
            public float FrictionImpulse2;
            public uint FrameId;
        }
    }

    // All struct definitions
    [StructLayout(LayoutKind.Sequential)]
    public struct BroadphaseCell
    {
        public uint StartIndex;
        public uint Count;
        public uint MaxObjects;
        public uint Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BroadphaseObject
    {
        public uint BodyId;
        public uint CellHash;
        public Vector3 MinBounds;
        public float Padding0;
        public Vector3 MaxBounds;
        public float Padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Manifold
    {
        public uint BodyA;
        public uint BodyB;
        public Vector3 Normal;
        public float Depth;
        public Vector3 ContactPoint1;
        public Vector3 ContactPoint2;
        public Vector3 ContactPoint3;
        public Vector3 ContactPoint4;
        public uint ContactCount;
        public Vector3 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactSolverConstants
    {
        public float DeltaTime;
        public uint Iteration;
        public uint TotalIterations;
        public float BaumgarteFactor;
        public float BaumgarteSlop;
        public float MaxCorrection;
        public float WarmStartFactor;
        public float RelaxationFactor;
        public uint CollisionCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PositionIntegrationConstants
    {
        public float DeltaTime;
        public uint BodyCount;
        public Vector3 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BroadphasePushConstants
    {
        public float CellSize;
        public uint GridSize;
        public uint MaxObjectsPerCell;
        public uint BodyCount;
        public uint ColliderCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPURigidbody
    {
        public Vector3 Position;
        public float Padding0;
        public Vector4 Rotation;
        public Vector3 Velocity;
        public float Padding1;
        public Vector3 AngularVelocity;
        public float Padding2;
        public Vector3 Force;
        public float Padding3;
        public Vector3 Torque;
        public float Padding4;
        public float Mass;
        public float InverseMass;
        public float Restitution;
        public float Friction;
        public uint BodyType;
        public uint ColliderIndex;
        public uint IsActive;
        public uint Padding5;
        public Matrix4x4 InertiaTensor;
        public Matrix4x4 InverseInertiaTensor;
        public Vector3 CenterOfMass;
        public float LinearDamping;
        public float AngularDamping;
        private Vector3 _padding5;
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct GPUCollider
    {
        public Vector3 Position;
        public float Padding0;
        public Vector4 Rotation;
        public Vector3 Size;
        public float Padding1;
        public float Radius;
        public float Height;
        public uint Shape;
        public uint IsTrigger;
        public uint RigidbodyIndex;
        public uint Padding2;
        public Matrix4x4 WorldMatrix;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUParticle
    {
        public Vector3 Position;
        public float Lifetime;
        public Vector3 Velocity;
        public float Size;
        public Vector4 Color;
        public uint IsActive;
        public Vector3 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUConstraint
    {
        public uint BodyA;
        public uint BodyB;
        public Vector3 LocalAnchorA;
        public float Padding0;
        public Vector3 LocalAnchorB;
        public float Padding1;
        public float MinDistance;
        public float MaxDistance;
        public float Stiffness;
        public float Damping;
        public uint ConstraintType;
        public Vector3 Padding2;
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct CollisionPair
    {
        public uint BodyA;
        public uint BodyB;
        public uint ColliderA;
        public uint ColliderB;
        public Vector3 Normal;
        public float Depth;
        public Vector3 ContactPoint;
        public float Impulse;
        public Vector3 Tangent1;
        private float _padding2;
        public Vector3 Tangent2;
        private float _padding3;
        public float FrictionImpulse1;
        public float FrictionImpulse2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PhysicsParams
    {
        public Vector4 Gravity;
        public float DeltaTime;
        public uint ParticleCount;
        public uint BodyCount;
        public uint ColliderCount;
        public uint ConstraintCount;
        public uint MaxCollisionPairs;
        public float RestitutionScale;
        public float ContactBias;
        public float ContactSlop;
        public Vector2 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PhysicsPushConstants
    {
        public float DeltaTime;
        public float Gravity;
        public uint ParticleCount;
        public uint BodyCount;
        public uint ColliderCount;
        public uint ConstraintCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GJKSimplex
    {
        public Vector3 Points0;
        public float Weight0;
        public Vector3 Points1;
        public float Weight1;
        public Vector3 Points2;
        public float Weight2;
        public Vector3 Points3;
        public float Weight3;
        public int Count;
        public Vector3 Direction;
        public float ClosestDistance;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EPATriangle
    {
        public Vector3 Normal;
        public float Distance;
        public Vector3 Points0;
        public Vector3 Points1;
        public Vector3 Points2;
        public int IsValid;
        public Vector3 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SATResult
    {
        public Vector3 Axis;
        public float Overlap;
        public int IsSeparated;
        public Vector3 ContactPoint;
        public Vector3 EdgePointA;
        public Vector3 EdgePointB;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CCDResult
    {
        public uint BodyA;
        public uint BodyB;
        public float TimeOfImpact;
        public Vector3 Point;
        public Vector3 Normal;
        public uint HasCollision;
        public Vector3 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactPoint
    {
        public Vector3 Position;
        public Vector3 Normal;
        public float Depth;
        public float WarmStartImpulse;
        public float FrictionImpulse1;
        public float FrictionImpulse2;
        public uint FeatureType;
        public Vector3 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PersistentContact
    {
        public Vector3 Position;
        public Vector3 Normal;
        public float Depth;
        public float AccumulatedImpulse;
        public float AccumulatedFriction1;
        public float AccumulatedFriction2;
        public uint Lifetime;
        public uint IsValid;
        public Vector3 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EnhancedBroadphaseConstants
    {
        public float CellSize;
        public uint GridSize;
        public uint MaxObjectsPerCell;
        public uint BodyCount;
        public uint ColliderCount;
        public float TimeStep;
        public float ExpansionFactor;
        public uint EnableTemporalCoherence;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GJKConstants
    {
        public uint MaxIterations;
        public float Epsilon;
        public uint MaxSimplexSize;
        public uint EnableMinkowski;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EPAConstants
    {
        public uint MaxIterations;
        public float Epsilon;
        public uint MaxTriangles;
        public float DepthTolerance;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SATConstants
    {
        public uint MaxFaceChecks;
        public uint MaxEdgeChecks;
        public float Epsilon;
        public float FeatureSwapThreshold;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactGenerationConstants
    {
        public uint MaxContactsPerPair;
        public float ContactMergeDistance;
        public float PersistentThreshold;
        public float NormalTolerance;
        public uint EnableWarmStarting;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FrictionSolverConstants
    {
        public float DeltaTime;
        public uint Iteration;
        public uint FrictionIterations;
        public float StaticFrictionThreshold;
        public float DynamicFrictionScale;
        public float FrictionStiffness;
        public uint CollisionCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CCDConstants
    {
        public float DeltaTime;
        public uint MaxSteps;
        public float Tolerance;
        public float VelocityThreshold;
        public uint EnableLinearCCD;
        public uint EnableAngularCCD;
    }

    public enum CollisionEventType
    {
        Enter,
        Stay,
        Exit
    }

    public class CollisionEvent
    {
        public uint BodyA { get; set; }
        public uint BodyB { get; set; }
        public uint ColliderA { get; set; }
        public uint ColliderB { get; set; }
        public Vector3 Normal { get; set; }
        public float Depth { get; set; }
        public Vector3 ContactPoint { get; set; }
        public CollisionEventType Type { get; set; }
        public float Impulse { get; internal set; }
    }

    internal struct ParticleEmissionRequest
    {
        public uint ParticleSystemId;
        public uint Count;
        public Vector4 Position;
        public Vector4 BaseVelocity;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct ContactSolverParams
    {
        public float DeltaTime;
        public uint Iteration;
        public uint TotalIterations;
        public float BaumgarteFactor;
        public float BaumgarteSlop;
        public float MaxCorrection;
        public float WarmStartFactor;
        public float RelaxationFactor;
        public Vector3 Padding;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct CollisionResolutionData
    {
        public Vector3 Impulse;
        public Vector3 TangentImpulse1;
        public Vector3 TangentImpulse2;
        public float AppliedImpulseMagnitude;
        public uint IsResolved;
    }
}