using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class CameraRenderer : IComponentRenderer<Camera>, IDisposable
    {
        private UniformBufferObject? _ubo;
        private bool _isInitialized = false;

        public unsafe ValueTask InitializeAsync(Camera component, VulkanContext context)
        {
            if (_isInitialized)
            {
                return new ValueTask();
            }

            _ubo = UniformBufferObject.Create(context, (ulong)Marshal.SizeOf<Matrix4x4>(), "CameraData");
            context.PipelineManager.SetBuffer(_ubo,0, 0);
            _isInitialized = true;
            return new ValueTask();
        }

        public async Task RenderAsync(Camera component, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            var viewProjectionMatrix = component.ViewProjectionMatrix;

            await _ubo!.UniformBuffer.SendDataAsync(viewProjectionMatrix);
        }


        public void Dispose()
        {
            _ubo?.Dispose();
        }

    }
}