using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.PipelineRenderers
{
    public interface IRenderPipeline : IDisposable
    {
        Task Execute(VkCommandBuffer cmd, CameraManager cameraManager, Renderer renderer);

        Task Update();
    }
}
