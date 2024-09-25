using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering
{
    public sealed class EngineRenderPass :IDisposable
    {
        public RenderPassType Type { get; }
        public VkRenderPass RenderPass { get; }
        public EngineRenderPass(RenderPassType renderPassType, VkRenderPass renderPass)
        {
            Type = renderPassType;
            RenderPass = renderPass;
        }

        public void Dispose() => RenderPass.Dispose();
    }
}
