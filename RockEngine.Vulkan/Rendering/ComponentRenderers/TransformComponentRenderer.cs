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
        private readonly PipelineManager _pipelineManager;

        public TransformComponentRenderer(VulkanContext context, PipelineManager pipelineManager)
        {
            _context = context;
            _pipelineManager = pipelineManager;
        }

        public ValueTask InitializeAsync(TransformComponent component)
        {
            if (_isInitialized)
            {
                return new ValueTask();
            }

            _ubo = UniformBufferObject.Create(_context, (ulong)Marshal.SizeOf<Matrix4x4>(), "Model");
            _pipelineManager.SetBuffer(_ubo, 1,0);
            _isInitialized = true;
            return new ValueTask();
        }

        public async ValueTask RenderAsync(TransformComponent component, FrameInfo frameInfo)
        {
            if (!component.Entity.TryGet<MeshComponent>(out var _))
            {
                return;
            }
            var model = component.GetModelMatrix();
            await _ubo.UniformBuffer.SendDataAsync(model);
            _pipelineManager.Use(_ubo, frameInfo);
        }

        public ValueTask UpdateAsync(TransformComponent component)
        {
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _ubo.Dispose();
        }

        
    }
}