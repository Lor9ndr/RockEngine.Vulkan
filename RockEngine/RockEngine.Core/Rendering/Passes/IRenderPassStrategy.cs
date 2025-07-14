using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.SubPasses;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public interface IRenderPassStrategy : IDisposable
    {
        IReadOnlyCollection<IRenderSubPass> SubPasses { get; }
        int Order { get; }
        EngineRenderPass BuildRenderPass(GraphicsEngine graphicsEngine);
        void InitializeSubPasses();
        Task Execute(VkCommandBuffer cmd, CameraManager cameraManager, Renderer renderer);
        Task Update();
    }
}
