using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using ZLinq;

namespace RockEngine.Core.Rendering.Passes
{
    public class SwapchainPassStrategy : PassStrategyBase
    {
        public override int Order => int.MaxValue;

        public SwapchainPassStrategy(VulkanContext context, IEnumerable<IRenderSubPass> subPasses)
            : base(context, subPasses)
        {
        }

        public override ValueTask Execute(SubmitContext submitContext, CameraManager cameraManager, WorldRenderer renderer)
        {
            using (PerformanceTracer.BeginSection(nameof(SwapchainPassStrategy)))
            {
                var primaryBatch = submitContext.CreateBatch();

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