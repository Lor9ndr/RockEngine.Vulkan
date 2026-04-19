using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes.SubPasses;

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
        ValueTask Execute(RenderContext renderContext, WorldRenderer renderer);
        ValueTask Update();
    }
}
