using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class CameraManager
    {
        private readonly VulkanContext _context;
        private readonly GraphicsEngine _engine;
        private readonly List<Camera> _activeCameras = new List<Camera>();
        private readonly EngineRenderPass _deferredRenderPass;

        public IReadOnlyList<Camera> ActiveCameras => _activeCameras;

        public CameraManager(VulkanContext context, GraphicsEngine engine, EngineRenderPass deferredRenderPass)
        {
            _context = context;
            _engine = engine;
            _deferredRenderPass = deferredRenderPass;
        }

        public void Register(Camera camera, Renderer renderer)
        {
            if (camera.RenderTarget == null)
            {
                camera.RenderTarget = new CameraRenderTarget(
                    _context,
                    _engine,
                    new Silk.NET.Vulkan.Extent2D(1920, 1080),
                    _deferredRenderPass
                );
                _engine.Swapchain.OnSwapchainRecreate += (swapchain) =>
                {
                    camera.RenderTarget.Resize(swapchain.Extent);
                };
                camera.RenderTarget.CreateFramebuffers();
                InitializeGBuffer(camera.RenderTarget.GBuffer, renderer);
            }

            _activeCameras.Add(camera);
        }

        private void InitializeGBuffer(GBuffer gbuffer, Renderer renderer)
        {
            // Исправляем передачу параметров
            gbuffer.CreateLightingDescriptorSets(renderer.DeferredLightingPipeline);
            gbuffer.Material.Bindings.Add(new UniformBufferBinding(renderer.GlobalUbo, 0, 0));

            gbuffer.Material.Bindings.Add(
                new UniformBufferBinding(renderer.LightManager.CountLightUbo, 1, 1));
        }

        public void Unregister(Camera camera)
        {
            _activeCameras.Remove(camera);
            camera.RenderTarget?.Dispose();
        }
    }

}
