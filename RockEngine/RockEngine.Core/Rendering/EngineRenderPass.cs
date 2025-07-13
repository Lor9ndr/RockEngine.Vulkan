using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering
{
    public sealed class EngineRenderPass : IDisposable
    {
        public VkRenderPass RenderPass { get; }
        public EngineRenderPass(VkRenderPass renderPass)
        {
            RenderPass = renderPass;
        }

        public static implicit operator VkRenderPass(EngineRenderPass engineRenderPass)=> engineRenderPass.RenderPass;
        public void Dispose() => RenderPass.Dispose();
    }
}
