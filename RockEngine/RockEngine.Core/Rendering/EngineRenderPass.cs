using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering
{
    public sealed class EngineRenderPass : IDisposable
    {
        public string Name { get;set; }
        public VkRenderPass RenderPass { get; }
        public EngineRenderPass(string name, VkRenderPass renderPass)
        {
            Name = name;
            RenderPass = renderPass;
        }

        public static implicit operator VkRenderPass(EngineRenderPass engineRenderPass)=> engineRenderPass.RenderPass;
        public void Dispose() => RenderPass.Dispose();
    }
}
