using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public class DeferredPassStrategy : PassStrategyBase
    {
        private readonly GlobalUbo _globalUbo;

        public LightingPass LightingPass => (LightingPass)SubPasses.FirstOrDefault(s=>s.GetType() == typeof(LightingPass));

        public override int Order => int.MinValue;

        public DeferredPassStrategy(
            VulkanContext context,
            IEnumerable<IRenderSubPass> subpasses,
            GlobalUbo globalUbo)
            :base(context, subpasses)
        {
            _globalUbo = globalUbo;
        }

        public override async Task Execute(VkCommandBuffer cmd, CameraManager cameraManager, Renderer renderer)
        {
            for (int i = 0; i < cameraManager.ActiveCameras.Count; i++)
            {
                Camera? camera = cameraManager.ActiveCameras[i];
                await ExecuteCameraPass(cmd, camera, renderer, i);
            }

        }

        private async Task ExecuteCameraPass(VkCommandBuffer cmd, Camera camera, Renderer renderer, int camIndex)
        {
            camera.RenderTarget.PrepareForRender(cmd);
            unsafe
            {
                fixed (ClearValue* pClearValue = camera.RenderTarget.ClearValues)
                {
                    var renderPassBeginInfo = new RenderPassBeginInfo
                    {
                        SType = StructureType.RenderPassBeginInfo,
                        RenderPass = camera.RenderTarget.RenderPass,
                        Framebuffer = camera.RenderTarget.Framebuffers[renderer.FrameIndex],
                        RenderArea = camera.RenderTarget.Scissor,
                        ClearValueCount = (uint)camera.RenderTarget.ClearValues.Length,
                        PClearValues = pClearValue
                    };

                    cmd.BeginRenderPass(in renderPassBeginInfo, SubpassContents.SecondaryCommandBuffers);
                }
            }

            var buffers = cmd.CommandPool.AllocateCommandBuffers((uint)_subPasses.Length, CommandBufferLevel.Secondary);
            var subpassIndices = new[] { 0u, 1u, 2u };
            for (int i = 0; i < _subPasses.Length; i++)
            {
                var inheritanceInfo = new CommandBufferInheritanceInfo
                {
                    SType = StructureType.CommandBufferInheritanceInfo,
                    RenderPass = camera.RenderTarget.RenderPass,
                    Subpass = subpassIndices[i],
                    Framebuffer = camera.RenderTarget.Framebuffers[renderer.FrameIndex]
                };

                VkCommandBuffer childCmd = buffers[i];
                childCmd.Begin(CommandBufferUsageFlags.RenderPassContinueBit, in inheritanceInfo);

                childCmd.LabelObject($"ExecuteCameraPass [{i}] cmd");
                IRenderSubPass? subpass = _subPasses[i];
                using (cmd.NameAction(subpass.GetType().Name, [0.8f, 0.2f, 0.2f, 1.0f]))
                {
                    await subpass.Execute(childCmd, renderer.FrameIndex, camera, camIndex);
                    childCmd.End();
                    cmd.ExecuteSecondary(childCmd);
                    if (_subPasses.Length - 1 != i)
                    {
                        cmd.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                    }
                }
            }
          

            cmd.EndRenderPass();
            camera.RenderTarget.TransitionToRead(cmd);
            foreach (var item in buffers)
            {
                _context.SubmitContext.AddDependency(item);
            }
        }
    }
}
