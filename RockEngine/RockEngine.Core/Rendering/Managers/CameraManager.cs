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

        public IReadOnlyList<Camera> ActiveCameras => _activeCameras;

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
            gbuffer.Material.Bindings.Add(renderer.GlobalUbo.GetBinding((uint)_activeCameras.Count));

            gbuffer.Material.Bindings.Add(new UniformBufferBinding(renderer.LightManager.CountLightUbo, 1, 1));
        }

        public void Unregister(Camera camera)
        {
            _activeCameras.Remove(camera);
            camera.RenderTarget?.Dispose();
        }
    }

}
