using RockEngine.Core.Diagnostics;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using ZLinq;

namespace RockEngine.Core.Rendering.Passes
{
    public class SwapchainPassStrategy(VulkanContext vulkanContext, CameraManager cameraManager, IEnumerable<IRenderSubPass> subPasses) 
        : PassStrategyBase(vulkanContext, subPasses)
    {
        public override int Order => int.MaxValue;

        public override ValueTask Execute(RenderContext context, WorldRenderer renderer)
        {
            using (PerformanceTracer.BeginSection(nameof(SwapchainPassStrategy)))
            {
                var primaryBatch = context.GraphicsContext.CreateBatch();

                foreach (var item in _subPasses)
                {
                    item.Execute(primaryBatch, renderer, cameraManager.RegisteredCameras.ToList());
                }

                primaryBatch.Submit();
            }
            return ValueTask.CompletedTask;
        }
    }
}