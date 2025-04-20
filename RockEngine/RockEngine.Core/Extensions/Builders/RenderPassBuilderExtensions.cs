using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan.Builders;

namespace RockEngine.Core.Extensions.Builders
{
    public static class RenderPassBuilderExtensions
    {
         public static EngineRenderPass Build(this RenderPassBuilder builder, RenderPassManager renderPassManager, string name = "RenderPass")
        {
            return renderPassManager.CreateRenderPass(builder.Build(), name);
        }
    }
}
