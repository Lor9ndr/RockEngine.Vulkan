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
        private readonly PipelineManager _pipelineManager;

        public CameraRenderer(VulkanContext context, PipelineManager pipelineManager)
        {
            _context = context;
            _pipelineManager = pipelineManager;
        }

        public unsafe ValueTask InitializeAsync(Camera component)
        {
            if (_isInitialized)
            {
                return new ValueTask();
            }

            _ubo = UniformBufferObject.Create(_context, (ulong)Marshal.SizeOf<CameraData>(), "CameraData");
            _pipelineManager.SetBuffer(_ubo,0, 0);
            _isInitialized = true;
            return new ValueTask();
        }

        public async ValueTask RenderAsync(Camera component, FrameInfo frameInfo)
        {
            var viewProjectionMatrix = component.ViewProjectionMatrix;
            var cameraData = new CameraData()
            {
                viewproj = viewProjectionMatrix,
                viewPos = component.Entity.Transform.Position
            };

            // CAN BE MOVED TO THE UPDATE METHOD
            await _ubo!.UniformBuffer.SendDataAsync(cameraData);

            // THIS IS HAS TO BE STAYED HERE 
            _pipelineManager.Use(_ubo, frameInfo);
        }


        public void Dispose()
        {
            _ubo?.Dispose();
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct CameraData
        {
            public Matrix4x4 viewproj;
            public Vector3 viewPos;
        }
    }
   
}