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
        private readonly VulkanContext _context;

        public CameraRenderer(VulkanContext context)
        {
            _context = context;
        }

        public unsafe ValueTask InitializeAsync(Camera component)
        {
            if (_isInitialized)
            {
                return new ValueTask();
            }

            _ubo = UniformBufferObject.Create(_context, (ulong)Marshal.SizeOf<Matrix4x4>(), "CameraData");
            _context.PipelineManager.SetBuffer(_ubo,0, 0);
            _isInitialized = true;
            return new ValueTask();
        }

        public async Task RenderAsync(Camera component, CommandBufferWrapper commandBuffer)
        {
            var viewProjectionMatrix = component.ViewProjectionMatrix;

            _context.PipelineManager.Use(_ubo, commandBuffer);
            await _ubo!.UniformBuffer.SendDataAsync(viewProjectionMatrix);
        }


        public void Dispose()
        {
            _ubo?.Dispose();
        }

    }
}