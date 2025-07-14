using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public class SwapchainPassStrategy : PassStrategyBase
    {
        public override int Order => int.MaxValue;

        public SwapchainPassStrategy(VulkanContext context, IEnumerable<IRenderSubPass> subPasses)
            : base(context, subPasses)
        {
        }

      
        public override async Task Execute(VkCommandBuffer cmd, CameraManager cameraManager, Renderer renderer)
        {
            renderer.SwapchainTarget.PrepareForRender(cmd);
            using (cmd.NameAction("Screen composition", [0.7f, 0.7f, 0.7f, 1.0f]))
            {
                unsafe
                {
                    fixed (ClearValue* pClearValue = renderer.SwapchainTarget.ClearValues)
                    {
                        var swapchainBeginInfo = new RenderPassBeginInfo
                        {
                            SType = StructureType.RenderPassBeginInfo,
                            RenderPass = _renderPass.RenderPass,
                            Framebuffer = renderer.SwapchainTarget.Framebuffers[renderer.FrameIndex],
                            RenderArea = new Rect2D { Extent = renderer.SwapchainTarget.Size },
                            ClearValueCount = (uint)renderer.SwapchainTarget.ClearValues.Length,
                            PClearValues = pClearValue
                        };

                        cmd.BeginRenderPass(in swapchainBeginInfo, SubpassContents.SecondaryCommandBuffers);
                    }
                }


                // Subpass 0: Screen composition
                var childCmd = cmd.CommandPool.AllocateCommandBuffer(CommandBufferLevel.Secondary);
                var inheritanceInfo = new CommandBufferInheritanceInfo()
                {
                    SType = StructureType.CommandBufferInheritanceInfo,
                    RenderPass = _renderPass.RenderPass,
                    Subpass = 0,
                    Framebuffer = renderer.SwapchainTarget.Framebuffers[renderer.FrameIndex]
                };

                childCmd.Begin(CommandBufferUsageFlags.RenderPassContinueBit, in inheritanceInfo);
                foreach (var item in _subPasses)
                {
                    using (childCmd.NameAction(item.GetType().Name, [0.7f, 0.7f, 0.7f, 1.0f]))
                    {
                        await item.Execute(childCmd, renderer, cameraManager.ActiveCameras[0]);
                    }

                }

                childCmd.End();
                cmd.ExecuteSecondary(childCmd);

                cmd.EndRenderPass();
                _context.SubmitContext.AddDependency(childCmd);
            }
        }
    }
}
