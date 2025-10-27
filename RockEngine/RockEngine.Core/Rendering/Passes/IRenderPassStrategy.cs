using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public interface IRenderPassStrategy : IDisposable
    {
        IReadOnlyCollection<IRenderSubPass> SubPasses { get; }
        List<AttachmentDescription> Attachments { get; }
        int Order { get; }

        RckRenderPass? RenderPass { get; }
        RckRenderPass BuildRenderPass();
        void InitializeSubPasses();
        Task Execute(SubmitContext submitContext,CameraManager cameraManager, Renderer renderer);
        ValueTask Update();
    }
}
