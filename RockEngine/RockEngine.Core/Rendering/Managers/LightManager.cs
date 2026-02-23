using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using ZLinq;

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
                   1,
                   1
                );
            }

            _countLightUbo = new UniformBuffer(sizeof(uint));

            _countLightBinding = new UniformBufferBinding(_countLightUbo, 0, 1);
        }

        public void RegisterLight(Light light) => _activeLights.Add(light);
        public void UnregisterLight(Light light) => _activeLights.Remove(light);

        public ValueTask UpdateAsync(uint frameIndex)
        {
            var frameBuffer = _lightBuffers[frameIndex];
            if (_activeLights.Count == 0)
            {
                return ValueTask.CompletedTask;
            }
            var lightData = new LightData[_activeLights.Count];
            for (int i = 0; i < _activeLights.Count; i++)
            {
                lightData[i] = _activeLights[i].GetLightData();
            }

            var batch = _context.TransferSubmitContext.CreateBatch();
            batch.LabelObject("Lightmanager cmd");
            frameBuffer.StageData(batch, lightData);

            // Update light count UBO
            var lightCountData = new[] { _activeLights.Count };
            batch.StageToBuffer(
                lightCountData,
                _countLightUbo.Buffer,
                0,
                (ulong)(sizeof(int) * lightCountData.Length)
            );


            batch.Submit();


            return ValueTask.CompletedTask;
        }
        public IEnumerable<Light> GetShadowCastingLights() => _activeLights.AsValueEnumerable().Where(s => s.CastShadows == true).ToList();

        public void Dispose()
        {
            foreach (var buffer in _lightBuffers)
            {
                buffer.Dispose();
            }
            _countLightUbo.Dispose();
        }

        internal StorageBuffer<LightData> GetCurrentLightBuffer(uint frameIndex)
        {
            return _lightBuffers[frameIndex];
        }

        internal StorageBufferBinding<LightData> GetCurrentLightBufferBinding(uint frameIndex)
        {
            return _lightBindings[frameIndex];
        }
        internal UniformBufferBinding GetCountLightBufferBinding()
        {
            return _countLightBinding;
        }


    }

}
