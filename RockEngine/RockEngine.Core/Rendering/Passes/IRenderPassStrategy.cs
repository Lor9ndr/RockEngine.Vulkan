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
        void Execute(UploadBatch batch, CameraManager cameraManager, Renderer renderer);
        ValueTask Update();
    }
}
