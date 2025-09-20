using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.SubPasses;
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

        public override Task Execute(SubmitContext submitContext, CameraManager cameraManager, Renderer renderer)
        {
            using (PerformanceTracer.BeginSection(nameof(SwapchainPassStrategy)))
            {
                var primaryBatch = submitContext.CreateBatch();
                var cmd = primaryBatch.CommandBuffer;
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

                    // Create a secondary batch for screen composition
                    var inheritanceInfo = new CommandBufferInheritanceInfo()
                    {
                        SType = StructureType.CommandBufferInheritanceInfo,
                        RenderPass = _renderPass.RenderPass,
                        Subpass = 0,
                        Framebuffer = renderer.SwapchainTarget.Framebuffers[renderer.FrameIndex]
                    };

                    var secondaryBatch = submitContext.CreateBatch(new BatchCreationParams
                    {
                        Level = CommandBufferLevel.Secondary,
                        InheritanceInfo = inheritanceInfo
                    });

                    var childCmd = secondaryBatch.CommandBuffer;
                    using (childCmd.NameAction("Screen composition subpass", [0.7f, 0.7f, 0.7f, 1.0f]))
                    {
                        foreach (var item in _subPasses)
                        {
                            using (childCmd.NameAction(item.GetType().Name, [0.7f, 0.7f, 0.7f, 1.0f]))
                            {
                                item.Execute(childCmd, renderer, cameraManager.ActiveCameras.FirstOrDefault());
                            }
                        }
                    }

                    // End the secondary batch after recording
                    secondaryBatch.End();

                    // Execute the secondary batch using the batch system
                    primaryBatch.ExecuteCommands(secondaryBatch);

                    cmd.EndRenderPass();
                }

                primaryBatch.Submit();
                return Task.CompletedTask;
            }
        }
    }
}