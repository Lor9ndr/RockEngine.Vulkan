using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class LightManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly StorageBuffer<LightData>[] _lightBuffers;
        private readonly UniformBuffer _countLightUbo;
        private readonly UniformBufferBinding _countLightBinding;
        private readonly StorageBufferBinding<LightData>[] _lightBindings;
        private readonly List<Light> _activeLights = new List<Light>();
        private int _currentFrameIndex;

        public UniformBuffer CountLightUbo => _countLightUbo;

        public LightManager(VulkanContext context, uint maxFramesInFlight, ulong maxLights)
        {
            _context = context;
            _lightBuffers = new StorageBuffer<LightData>[maxFramesInFlight];

            _lightBindings = new StorageBufferBinding<LightData>[maxFramesInFlight];
            for (int i = 0; i < maxFramesInFlight; i++)
            {
                _lightBuffers[i] = new StorageBuffer<LightData>(context, maxLights);
                _lightBindings[i] = new StorageBufferBinding<LightData>(
                    _lightBuffers[i],
                   0,
                   1
                );
            }

            _countLightUbo = new UniformBuffer("LightCount", 1, sizeof(uint), sizeof(uint));

            _countLightBinding = new UniformBufferBinding(_countLightUbo, 1, 1);
        }

        public void RegisterLight(Light light) => _activeLights.Add(light);
        public void UnregisterLight(Light light) => _activeLights.Remove(light);

        public Task UpdateAsync(IEnumerable<Camera> cameras)
        {
            var frameBuffer = _lightBuffers[_currentFrameIndex];
            if (_activeLights.Count == 0)
            {
                return Task.CompletedTask;
            }
            var lightData = new LightData[_activeLights.Count];
            for (int i = 0; i < _activeLights.Count; i++)
            {
                lightData[i] = _activeLights[i].GetLightData();
            }

            var batch = _context.SubmitContext.CreateBatch();
            batch.CommandBuffer.LabelObject("Lightmanager cmd");
            frameBuffer.StageData(batch, lightData);

            // Update light count UBO
            var lightCountData = new[] { _activeLights.Count };
            batch.StageToBuffer(
                lightCountData.AsSpan(),
                _countLightUbo.Buffer,
                0,
                (ulong)(sizeof(int) * lightCountData.Length)
            );

            batch.Submit();

            // Update camera materials
            foreach (var camera in cameras)
            {
                camera.RenderTarget.GBuffer.Material.Bindings.Add(_countLightBinding);
                camera.RenderTarget.GBuffer.Material.Bindings.Add(_lightBindings[_currentFrameIndex]);
            }

            _currentFrameIndex = (_currentFrameIndex + 1) % _lightBuffers.Length;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var buffer in _lightBuffers)
            {
                buffer.Dispose();
            }
            _countLightUbo.Dispose();
        }

        internal StorageBuffer<LightData> GetCurrentLightBuffer()
        {
            return _lightBuffers[_currentFrameIndex];
        }

            internal StorageBufferBinding<LightData> GetCurrentLightBufferBinding()
        {
            return _lightBindings[_currentFrameIndex];
        }
    }

}
