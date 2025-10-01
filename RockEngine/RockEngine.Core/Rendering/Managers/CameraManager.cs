using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using ZLinq;

namespace RockEngine.Core.Rendering.Managers
{
    public class CameraManager
    {
        private readonly VulkanContext _context;
        private readonly GraphicsEngine _engine;
        private readonly RenderPassManager _renderPassManager;
        private readonly PipelineManager _pipelineManager;
        private readonly List<Camera> _activeCameras = new List<Camera>();
        
        //TODO: OPTIMIZE THAT
        public ValueEnumerable<ZLinq.Linq.ListWhere<Camera>, Camera> ActiveCameras => _activeCameras.AsValueEnumerable().Where(s=>s.IsActive);

        public CameraManager(VulkanContext context, GraphicsEngine engine, RenderPassManager renderPassManager, PipelineManager pipelineManager)
        {
            _context = context;
            _engine = engine;
            _renderPassManager = renderPassManager;
            _pipelineManager = pipelineManager;
        }

        public void Register(Camera camera, Renderer renderer)
        {
            if (camera.RenderTarget == null)
            {
                camera.RenderTarget = new CameraRenderTarget(
                    _context,
                    _engine,
                    _engine.Swapchain.Extent);

                camera.RenderTarget.Initialize(_renderPassManager.GetRenderPass<DeferredPassStrategy>() ?? throw new Exception($"Unable to get renderPass of {nameof(DeferredPassStrategy)}"));
                InitializeGBuffer(camera.RenderTarget.GBuffer, renderer);
            }

            _activeCameras.Add(camera);
        }

        private void InitializeGBuffer(GBuffer gbuffer, Renderer renderer)
        {
            
            gbuffer.CreateLightingDescriptorSets(_pipelineManager.GetPipelineByName("DeferredLighting"));
            gbuffer.Material.BindResource(renderer.GlobalUbo.GetBinding((uint)_activeCameras.Count));

            gbuffer.Material.BindResource(new UniformBufferBinding(renderer.LightManager.CountLightUbo, 1, 1));
            gbuffer.Material.PushConstant("iblParams", new IBLParams());
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
