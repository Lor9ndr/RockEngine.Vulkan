using NLog;

using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Numerics;

namespace RockEngine.Core.Rendering.Managers
{
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

        private readonly Queue<int> _freeIndices = new Queue<int>();
        private readonly HashSet<int> _activeIndices = new HashSet<int>();
        private readonly Dictionary<int, int> _versionTracker = new Dictionary<int, int>();

        // NEW: Track mesh groups for consecutive allocation
        private readonly Dictionary<(Material Material, IMesh Mesh), List<int>> _meshGroupIndices = new Dictionary<(Material, IMesh), List<int>>();
        private readonly Dictionary<int, (Material Material, IMesh Mesh)> _indexToMeshGroup = new Dictionary<int, (Material, IMesh)>();

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public TransformManager(VulkanContext context, uint maxFramesInFlight)
        {
            _context = context;
            _transformBuffers = new StorageBuffer<Matrix4x4>[maxFramesInFlight];
            _transformBindings = new StorageBufferBinding<Matrix4x4>[maxFramesInFlight];
            _frameVersions = new int[maxFramesInFlight];

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
        /// NEW: Allocate transform indices that are consecutive for the same mesh group
        /// </summary>
        public int AllocateTransformForMesh(Matrix4x4 transform, Material material, IMesh mesh)
        {
            var groupKey = (material, mesh);

            if (!_meshGroupIndices.TryGetValue(groupKey, out var indices))
            {
                indices = new List<int>();
                _meshGroupIndices[groupKey] = indices;
            }

            int index;

            // Try to reuse free indices first
            if (_freeIndices.Count > 0)
            {
                index = _freeIndices.Dequeue();
                _transforms[index] = transform;
            }
            else
            {
                if (_transforms.Count >= INITIAL_CAPACITY)
                {
                    throw new InvalidOperationException("Transform capacity exceeded");
                }

                index = _transforms.Count;
                _transforms.Add(transform);
            }

            _activeIndices.Add(index);
            _versionTracker[index] = _globalVersion;
            indices.Add(index);
            _indexToMeshGroup[index] = groupKey;

            _globalVersion++;

            // Sort indices to maintain consecutive ordering where possible
            indices.Sort();

            _logger.Debug($"Allocated transform index {index} for mesh {mesh.ID} with material {material.Name}");

            return index;
        }

        /// <summary>
        /// Removes a transform and makes its index available for reuse
        /// </summary>
        public void RemoveTransform(int index)
        {
            if (index < 0 || index >= _transforms.Count || !_activeIndices.Contains(index))
            {
                return;
            }

            _activeIndices.Remove(index);
            _freeIndices.Enqueue(index);
            _versionTracker.Remove(index);

            // Remove from mesh group tracking
            if (_indexToMeshGroup.TryGetValue(index, out var groupKey))
            {
                if (_meshGroupIndices.TryGetValue(groupKey, out var indices))
                {
                    indices.Remove(index);
                    if (indices.Count == 0)
                    {
                        _meshGroupIndices.Remove(groupKey);
                    }
                }
                _indexToMeshGroup.Remove(index);
            }

            _transforms[index] = Matrix4x4.Identity;
            _globalVersion++;
        }

        /// <summary>
        /// Gets consecutive transform indices for a mesh group
        /// </summary>
        public List<int> GetConsecutiveIndicesForMeshGroup(Material material, IMesh mesh)
        {
            var groupKey = (material, mesh);
            if (_meshGroupIndices.TryGetValue(groupKey, out var indices))
            {
                // Return sorted indices (they should already be sorted, but ensure it)
                return indices.OrderBy(x => x).ToList();
            }
            return new List<int>();
        }

        /// <summary>
        /// Checks if transform indices for a mesh group are consecutive
        /// </summary>
        public bool AreMeshGroupIndicesConsecutive(Material material, IMesh mesh)
        {
            var indices = GetConsecutiveIndicesForMeshGroup(material, mesh);
            if (indices.Count <= 1) return true;

            for (int i = 1; i < indices.Count; i++)
            {
                if (indices[i] != indices[i - 1] + 1)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Updates existing transform and marks data as dirty
        /// </summary>
        public void UpdateTransform(int index, Matrix4x4 newTransform)
        {
            if (index < 0 || index >= _transforms.Count || !_activeIndices.Contains(index))
            {
                return;
            }

            _transforms[index] = newTransform;
            _versionTracker[index] = _globalVersion;
            _globalVersion++;
        }

        /// <summary>
        /// Gets only the active transforms for buffer updates
        /// </summary>
        private List<Matrix4x4> GetActiveTransforms()
        {
            var activeTransforms = new List<Matrix4x4>(_activeIndices.Count);
            foreach (var index in _activeIndices)
            {
                activeTransforms.Add(_transforms[index]);
            }
            return activeTransforms;
        }

        /// <summary>
        /// Updates GPU buffers only if changes exist for current frame
        /// </summary>
        public async ValueTask UpdateAsync(uint currentFrameIndex)
        {
            int frameVersion = _frameVersions[currentFrameIndex];
            if (_globalVersion == frameVersion)
            {
                return;
            }

            var buffer = _transformBuffers[currentFrameIndex];
            var activeTransforms = GetActiveTransforms();

            var batch = _context.GraphicsSubmitContext.CreateBatch();

            if (buffer.Capacity < (ulong)activeTransforms.Count)
            {
                buffer.Resize((ulong)Math.Max(activeTransforms.Count * 2, INITIAL_CAPACITY), batch);
            }

            // Barrier before update
            var preBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit,
                DstAccessMask = AccessFlags.TransferWriteBit,
                Buffer = buffer.Buffer,
                Offset = 0,
                Size = Vk.WholeSize
            };

            batch.PipelineBarrier(
                srcStage: PipelineStageFlags.VertexInputBit,
                dstStage: PipelineStageFlags.TransferBit,
                bufferMemoryBarriers: new[] { preBarrier }
            );

            buffer.StageData(batch, activeTransforms.ToArray());

            // Barrier after update
            var postBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit,
                Buffer = buffer.Buffer,
                Offset = 0,
                Size = Vk.WholeSize
            };

            batch.PipelineBarrier(
                srcStage: PipelineStageFlags.TransferBit,
                dstStage: PipelineStageFlags.VertexInputBit,
                bufferMemoryBarriers: new[] { postBarrier }
            );

            batch.Submit();
            _frameVersions[currentFrameIndex] = _globalVersion;
        }

        /// <summary>
        /// Checks if a transform index is still active/valid
        /// </summary>
        public bool IsTransformActive(int index)
        {
            return index >= 0 && index < _transforms.Count && _activeIndices.Contains(index);
        }

        /// <summary>
        /// Gets binding information for current frame
        /// </summary>
        public StorageBufferBinding<Matrix4x4> GetCurrentBinding(uint currentFrameIndex)
            => _transformBindings[currentFrameIndex];

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