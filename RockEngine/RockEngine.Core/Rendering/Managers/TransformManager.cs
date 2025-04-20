using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using System.Numerics;

namespace RockEngine.Core.Rendering.Managers
{
    public class TransformManager : IDisposable
    {
        public const int Capacity = 10_000;
        private readonly VulkanContext _context;
        private readonly StorageBuffer<Matrix4x4>[] _transformBuffers;
        private readonly List<Matrix4x4> _currentTransforms = new List<Matrix4x4>();
        private readonly StorageBufferBinding<Matrix4x4>[] _transformBuffersBindings;

        public TransformManager(VulkanContext context, uint maxFramesInFlight)
        {
            _context = context;
            _transformBuffers = new StorageBuffer<Matrix4x4>[maxFramesInFlight];
            _transformBuffersBindings = new StorageBufferBinding<Matrix4x4>[maxFramesInFlight];
            for (int i = 0; i < maxFramesInFlight; i++)
            {
                _transformBuffers[i] = new StorageBuffer<Matrix4x4>(context, Capacity);
                _transformBuffersBindings[i] = new StorageBufferBinding<Matrix4x4>(_transformBuffers[i],
                0,
                1);
            }
           
        }

        public void AddTransforms(IEnumerable<Matrix4x4> transforms)
        {
            _currentTransforms.AddRange(transforms);
        }
        public int AddTransform(Matrix4x4 transform)
        {
            _currentTransforms.Add(transform);
            return _currentTransforms.Count - 1;
        }

        public async Task Update(uint currentFrameIndex)
        {
            var currentBuffer = _transformBuffers[currentFrameIndex];
            var batch = _context.SubmitContext.CreateBatch();
            currentBuffer.StageData(batch, _currentTransforms.ToArray());
            batch.Submit();

            _currentTransforms.Clear();
        }

        public StorageBufferBinding<Matrix4x4> GetCurrentBinding(uint currentFrameIndex)
        {
            return _transformBuffersBindings[currentFrameIndex];
        }


        public void Dispose()
        {
            foreach (var buffer in _transformBuffers)
            {
                buffer.Dispose();
            }
        }
    }

}