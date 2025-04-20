using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.RenderTargets
{
    public interface IRenderTarget
    {
        Extent2D Size { get;  }
        Format Format { get;  }
        VkFrameBuffer[] Framebuffers { get; }
        VkRenderPass RenderPass { get; }

        void PrepareForRender(VkCommandBuffer cmd);
        void TransitionToRead(VkCommandBuffer cmd);

        void CreateFramebuffers();
    }
}