using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class CameraManager
    {
        private readonly VulkanContext _context;
        private readonly GraphicsEngine _engine;
        private readonly RenderPassManager _renderPassManager;
        private readonly PipelineManager _pipelineManager;
        private readonly List<Camera> _activeCameras = new List<Camera>();
        
        public IReadOnlyList<Camera> RegisteredCameras => _activeCameras;

        public CameraManager(VulkanContext context, GraphicsEngine engine, RenderPassManager renderPassManager, PipelineManager pipelineManager)
        {
            _context = context;
            _engine = engine;
            _renderPassManager = renderPassManager;
            _pipelineManager = pipelineManager;
        }

        public int Register(Camera camera, Renderer renderer)
        {

            _activeCameras.Add(camera);
            return _activeCameras.Count;
        }


        public void Unregister(Camera camera)
        {
            _activeCameras.Remove(camera);
            camera.RenderTarget?.Dispose();
        }
        public struct IBLParams
        {
            public float exposure = 1.0f;     // [0.1 - 4.0] Typical HDR exposure range
            public float envIntensity = 1.0f; // [0.0 - 2.0] Environment map multiplier
            public float aoStrength = 1.0f;    // [0.0 - 2.0] Ambient occlusion effect strength

            public IBLParams()
            {
            }
        }
    }

}
