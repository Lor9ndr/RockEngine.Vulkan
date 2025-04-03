using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

namespace RockEngine.Core.ECS.Systems
{
    internal class LightSystem : ISystem
    {
        private readonly Renderer _renderer;
        private readonly VulkanContext _context;
        private readonly int _currentLightBufferIndex;
        private readonly UniformBuffer[] _lightBuffers;
        private readonly UniformBufferBinding[] _lightBindings;
        private readonly UniformBuffer _countLightUbo;
        private readonly UniformBufferBinding _countLightUboBinding;

        public int Priority => 300;

        public const ulong MAX_LIGHTS_SUPPORTED = 1024;

        public LightSystem(Renderer renderer)
        {
            _renderer = renderer;
            _context = VulkanContext.GetCurrent();
            _lightBuffers = new UniformBuffer[_context.MaxFramesPerFlight];
            _lightBindings = new UniformBufferBinding[_lightBuffers.Length];
            for (int i = 0; i < _lightBuffers.Length; i++)
            {
                _lightBuffers[i] = new UniformBuffer("Lights", 0, LightData.DataSize * MAX_LIGHTS_SUPPORTED, (int)LightData.DataSize);
            }

            _countLightUbo = new UniformBuffer("LightCount", 1, sizeof(uint), sizeof(uint));

            _countLightUboBinding = new UniformBufferBinding(_countLightUbo, 1, 1);
            renderer.GBuffer.Material.Bindings.Add(_countLightUboBinding);
        }


        public ValueTask Update(World world, float deltaTime)
        {
            /*var lights = world.GetComponentsWithEntities<Light>();
            LightData[] lightData = new LightData[lights.Count()];

            int i = 0;
            foreach (var (entity, light) in lights)
            {
                lightData[i++] = light.GetLightData(in entity);
            }

            await _countLightUbo.UpdateAsync(lightData.Count);

            var size = (ulong)lightData.Length * LightData.DataSize;
            var stagingBuffer = await VkBuffer.CreateAndCopyToStagingBuffer(_context, lightData, size);
            var currentUbo = _lightBuffers[_currentLightBufferIndex];

            _context.SubmitSingleTimeCommand(cmd => {
                var copy = new BufferCopy
                {
                    Size = size
                };

                VulkanContext.Vk.CmdCopyBuffer(cmd,
                    stagingBuffer,
                    currentUbo.Buffer,
                    1, ref copy);
            });
            stagingBuffer.Dispose();

            _currentLightBufferIndex = (_currentLightBufferIndex + 1) % _lightBuffers.Length;

            _renderer.SwitchLightBufferBinding(_lightBindings[_currentLightBufferIndex]);*/
            return default;

        }
    }
}
