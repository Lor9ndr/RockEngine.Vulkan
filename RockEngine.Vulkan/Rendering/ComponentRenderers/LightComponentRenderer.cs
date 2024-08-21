using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class LightComponentRenderer : IComponentRenderer<LightComponent>, IDisposable
    {
        private UniformBufferObject? _ubo;
        private bool _isInitialized = false;
        private readonly VulkanContext _context;
        private readonly PipelineManager _pipelineManager;

        public LightComponentRenderer(VulkanContext context, PipelineManager pipelineManager)
        {
            _context = context;
            _pipelineManager = pipelineManager;
        }

        public unsafe ValueTask InitializeAsync(LightComponent component)
        {
            if (_isInitialized)
            {
                return new ValueTask();
            }

            _ubo = UniformBufferObject.Create(_context, (ulong)Marshal.SizeOf<LightData>(), "LightData");
            _pipelineManager.SetBuffer(_ubo, Constants.LIGHT_SET, 0);
            _isInitialized = true;
            return new ValueTask();
        }

        public async ValueTask RenderAsync(LightComponent component, FrameInfo frameInfo)
        {
            var lightData = new LightData
            {
                position = component.Entity.Transform.Position,
                color = component.Color,
                intensity = component.Intensity,
                type = (int)component.Type
            };
            // Update light data
            await _ubo!.UniformBuffer.SendDataAsync(lightData);
            // Bind the light data
            _pipelineManager.Use(_ubo, frameInfo);
        }

        public void Dispose()
        {
            _ubo?.Dispose();
        }

        public ValueTask UpdateAsync(LightComponent component)
        {
           return ValueTask.CompletedTask;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        private struct LightData
        {
            public Vector3 position;
            private float _padding1;  // Padding to align to 16 bytes
            public Vector3 color;
            public float intensity;  // This will now be correctly aligned
            public int type;
        }

    }
}
