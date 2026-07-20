using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.RenderTargets
{
    public interface IRenderTarget
    {
        Extent2D Size { get; }
        Format Format { get; }
        VkFrameBuffer[] Framebuffers { get; }
        RckRenderPass RenderPass { get; }

        void PrepareForRender(UploadBatch batch, uint frameIndex);
        void TransitionToRead(UploadBatch batch, uint frameIndex);
        public void Initialize(RckRenderPass renderPass);
    }
}