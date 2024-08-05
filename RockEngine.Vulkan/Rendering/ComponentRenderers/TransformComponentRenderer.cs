using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class TransformComponentRenderer : IComponentRenderer<TransformComponent>, IDisposable
    {
        private bool _isInitialized = false;
        private UniformBufferObject _ubo;
        private readonly VulkanContext _context;

        public TransformComponentRenderer(VulkanContext context)
        {
            _context = context;
        }
        public ValueTask InitializeAsync(TransformComponent component)
        {
            if (_isInitialized)
            {
                return new ValueTask();
            }

            _ubo = UniformBufferObject.Create(_context, (ulong)Marshal.SizeOf<Matrix4x4>(), "Model");
            _context.PipelineManager.SetBuffer(_ubo, 1,0);
            _isInitialized = true;
            return new ValueTask();
        }

        public async Task RenderAsync(TransformComponent component, CommandBufferWrapper commandBuffer)
        {
            if (_context.PipelineManager.CurrentPipeline is null)
            {
                return;
            }
            var model = component.GetModelMatrix();
            await _ubo.UniformBuffer.SendDataAsync(model, 0);
            _context.PipelineManager.Use(_ubo, commandBuffer);
        }

        public void Dispose()
        {
            _ubo.Dispose();
        }
    }
}