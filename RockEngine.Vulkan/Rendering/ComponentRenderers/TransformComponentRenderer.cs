using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class TransformComponentRenderer : IComponentRenderer<Transform>, IDisposable
    {
        private bool _isInitialized = false;
        private UniformBufferObject _ubo;

        public ValueTask InitializeAsync(Transform component, VulkanContext context)
        {
            if (_isInitialized)
            {
                return new ValueTask();
            }

            _ubo = UniformBufferObject.Create(context, (ulong)Marshal.SizeOf<Matrix4x4>(), "Model");
            context.PipelineManager.SetBuffer(_ubo, 1,0);
            _isInitialized = true;
            return new ValueTask();
        }

        public async Task RenderAsync(Transform component, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            if (context.PipelineManager.CurrentPipeline is null)
            {
                return;
            }
            var model = component.GetModelMatrix();
            await _ubo.UniformBuffer.SendDataAsync(model, 0);
        }

        public void Dispose()
        {
            _ubo.Dispose();
        }
    }
}