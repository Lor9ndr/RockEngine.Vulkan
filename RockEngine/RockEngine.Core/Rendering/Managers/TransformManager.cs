using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using System.Numerics;

namespace RockEngine.Core.Rendering.Managers
{
    /// <summary>
    /// Manages transformation matrices storage and updates for Vulkan rendering
    /// Optimized to only update GPU buffers when changes occur
    /// </summary>
    public sealed class TransformManager : IDisposable
    {
        public const int INITIAL_CAPACITY = 10_000;
        private readonly VulkanContext _context;

        // Double buffered storage for frame data
        private readonly StorageBuffer<Matrix4x4>[] _transformBuffers;
        private readonly StorageBufferBinding<Matrix4x4>[] _transformBindings;

        // Main transform storage and version tracking
        private readonly List<Matrix4x4> _transforms = new List<Matrix4x4>(INITIAL_CAPACITY);
        private int _globalVersion;
        private readonly int[] _frameVersions;

        public TransformManager(VulkanContext context, uint maxFramesInFlight)
        {
            _context = context;
            _transformBuffers = new StorageBuffer<Matrix4x4>[maxFramesInFlight];
            _transformBindings = new StorageBufferBinding<Matrix4x4>[maxFramesInFlight];
            _frameVersions = new int[maxFramesInFlight];

            // Initialize buffers for each frame context
            for (int i = 0; i < maxFramesInFlight; i++)
            {
                _transformBuffers[i] = new StorageBuffer<Matrix4x4>(context, INITIAL_CAPACITY);
                _transformBindings[i] = new StorageBufferBinding<Matrix4x4>(
                    _transformBuffers[i],
                    0,
                    1
                );
            }
        }

        /// <summary>
        /// Adds multiple transforms and marks data as dirty
        /// </summary>
        public void AddTransforms(IEnumerable<Matrix4x4> transforms)
        {
            _transforms.AddRange(transforms);
            _globalVersion++;
        }

        /// <summary>
        /// Adds single transform and returns its index
        /// </summary>
        public int AddTransform(Matrix4x4 transform)
        {
            if (_transforms.Count >= INITIAL_CAPACITY)
            {
                throw new InvalidOperationException("Transform capacity exceeded");
            }

            _transforms.Add(transform);
            _globalVersion++;
            return _transforms.Count - 1;
        }

        /// <summary>
        /// Updates existing transform and marks data as dirty
        /// </summary>
        public void UpdateTransform(int index, Matrix4x4 newTransform)
        {
            if (index < 0 || index >= _transforms.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            _transforms[index] = newTransform;
            _globalVersion++;
        }

        /// <summary>
        /// Updates GPU buffers only if changes exist for current frame
        /// </summary>
        public Task UpdateAsync(uint currentFrameIndex)
        {
            int frameVersion = _frameVersions[currentFrameIndex];

            // Skip update if no changes since last frame update
            if (_globalVersion == frameVersion)
            {
                return Task.CompletedTask;
            }

            var buffer = _transformBuffers[currentFrameIndex];
            var batch = _context.SubmitContext.CreateBatch();

            // Update entire buffer (could optimize to update only changed ranges)
            buffer.StageData(batch, _transforms.ToArray());
            batch.Submit();

            // Update frame version tracking
            _frameVersions[currentFrameIndex] = _globalVersion;
            return Task.CompletedTask;

        }

        /// <summary>
        /// Gets binding information for current frame
        /// </summary>
        public StorageBufferBinding<Matrix4x4> GetCurrentBinding(uint currentFrameIndex)
            => _transformBindings[currentFrameIndex];

        /// <summary>
        /// Gets direct access to transform matrices (use with caution)
        /// </summary>
        public Matrix4x4[] Transforms => _transforms.ToArray();

        /// <summary>
        /// Current number of stored transforms
        /// </summary>
        public int TransformCount => _transforms.Count;

        public void Dispose()
        {
            foreach (var buffer in _transformBuffers)
            {
                buffer.Dispose();
            }
        }
    }
}