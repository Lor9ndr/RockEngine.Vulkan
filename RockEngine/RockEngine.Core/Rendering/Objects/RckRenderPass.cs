using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Objects
{
    public sealed class RckRenderPass : IDisposable
    {
        public VkRenderPass RenderPass { get; }
        public IRenderSubPass[] SubPasses { get; }
        public RckRenderPass(VkRenderPass renderPass,  IRenderSubPass[] subPasses)
        {
            RenderPass = renderPass;
            SubPasses = subPasses;
        }

        public static explicit operator VkRenderPass(RckRenderPass engineRenderPass)=> engineRenderPass.RenderPass;

        public static implicit operator RenderPass(RckRenderPass v)
        {
            return v.RenderPass;
        }

        public void Dispose() => RenderPass.Dispose();
    }
}
