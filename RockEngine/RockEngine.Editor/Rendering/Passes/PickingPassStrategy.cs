using RockEngine.Core;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Editor.EditorComponents;
using RockEngine.Editor.Rendering.RenderTargets;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Editor.Rendering.Passes
{
    public class PickingPassStrategy : PassStrategyBase
    {
        private readonly GlobalUbo _globalUbo;
        private readonly PickingRenderTarget _pickingRenderTarget;
        public override int Order => int.MinValue + 1;

        public PickingRenderTarget PickingRenderTarget => _pickingRenderTarget;

        public PickingPassStrategy(
             VulkanContext context,
             IEnumerable<IRenderSubPass> subpasses,
             GlobalUbo globalUbo)
             : base(context, subpasses)
        {
            _globalUbo = globalUbo;
            _pickingRenderTarget = new PickingRenderTarget(context, new Extent2D(1280, 720));
        }

        public override Task Execute(SubmitContext submitContext, CameraManager cameraManager, Renderer renderer)
        {
            uint frameIndex = renderer.FrameIndex;
            var cams = cameraManager.RegisteredCameras;
            var tasks = new List<Task>(cams.Count);

            for (int i = 0; i < cams.Count; i++)
            {
                Camera camera = cams[i];
                if (camera is not DebugCamera debugCamera)
                {
                    continue;
                }
                tasks.Add(ExecuteCameraPass(submitContext, debugCamera, renderer, i, frameIndex));
            }
            return Task.WhenAll(tasks);
        }

        private Task ExecuteCameraPass(SubmitContext submitContext, DebugCamera camera, Renderer renderer, int camIndex, uint frameIndex)
        {
            using (PerformanceTracer.BeginSection($"Picking Camera - {camera.Entity.Name}"))
            {
                var primaryBatch = submitContext.CreateBatch();
                var cmd = primaryBatch.CommandBuffer;
                using (cmd.NameAction("PickingPassStrategy", [0.2f, 0.4f, 0.2f, 1.0f]))
                {
                    using (PerformanceTracer.BeginSection($"PickingPassStrategy-{camera.Entity.Name}", cmd, frameIndex))
                    {
                        PickingRenderTarget.PrepareForRender(cmd);

                        // Begin render pass
                        BeginRenderPass(PickingRenderTarget, renderer, cmd);

                        // Since we only have one subpass, execute it directly in the primary command buffer
                        if (_subPasses.Length > 0)
                        {
                            _subPasses[0].Execute(cmd, renderer.FrameIndex, camera, camIndex, PickingRenderTarget);
                        }

                        cmd.EndRenderPass();
                        PickingRenderTarget.TransitionToRead(cmd);
                    }
                }

                primaryBatch.Submit();
                return Task.CompletedTask;
            }
        }

        private unsafe static void BeginRenderPass(PickingRenderTarget pickingRenderTarget, Renderer renderer, VkCommandBuffer cmd)
        {
            fixed (ClearValue* pClearValue = pickingRenderTarget.ClearValues)
            {
                var renderPassBeginInfo = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = pickingRenderTarget.RenderPass,
                    Framebuffer = pickingRenderTarget.Framebuffers[renderer.FrameIndex],
                    RenderArea = pickingRenderTarget.Scissor,
                    ClearValueCount = (uint)pickingRenderTarget.ClearValues.Length,
                    PClearValues = pClearValue
                };

                cmd.BeginRenderPass(in renderPassBeginInfo, SubpassContents.Inline); // Use Inline for single subpass
            }
        }

        public override void InitializeSubPasses()
        {
            base.InitializeSubPasses();
            PickingRenderTarget.Initialize(RenderPass);
        }
    }
}