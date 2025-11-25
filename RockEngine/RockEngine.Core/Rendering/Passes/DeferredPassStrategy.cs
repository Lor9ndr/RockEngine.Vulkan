using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using ZLinq;

namespace RockEngine.Core.Rendering.Passes
{
    public class DeferredPassStrategy : PassStrategyBase
    {
        private readonly GlobalUbo _globalUbo;
        private readonly List<Task> _renderTasks;

        public LightingPass LightingPass => SubPasses.OfType<LightingPass>().First();
        public override int Order => 0;

        public DeferredPassStrategy(
             VulkanContext context,
             IEnumerable<IRenderSubPass> subpasses,
             GlobalUbo globalUbo)
             : base(context, subpasses)
        {
            _globalUbo = globalUbo;
            _renderTasks =  new List<Task>(4);

        }

        public override async ValueTask Execute(SubmitContext submitContext, CameraManager cameraManager, WorldRenderer renderer)
        {
            uint frameIndex = renderer.FrameIndex;
            var cams = cameraManager.RegisteredCameras;

            for (int i = 0; i < cams.Count; i++)
            {
                Camera camera = cams[i];
                if (camera.RenderTarget.RenderPass != RenderPass)
                {
                    continue;
                }
                if (camera.IsActive)
                {
                    _renderTasks.Add((ExecuteCameraPass(submitContext, camera, renderer, i, frameIndex)));
                }
            }
            await Task.WhenAll(_renderTasks);
            _renderTasks.Clear();
        }


        private Task ExecuteCameraPass(SubmitContext submitContext, Camera camera, WorldRenderer renderer, int camIndex, uint frameIndex)
        {

            var name = $"Camera - {camera.Entity.Name}";
            using (PerformanceTracer.BeginSection(name))
            {
                var primaryBatch = submitContext.CreateBatch();
                var cmd = primaryBatch.CommandBuffer;
                using (cmd.NameAction(name, [0.5f, 0.8f, 0.9f, 1.0f]))
                {
                    using (PerformanceTracer.BeginSection(name, cmd, frameIndex))
                    {
                        camera.RenderTarget.PrepareForRender(cmd);
                        BeginRenderPass(camera, renderer, cmd);

                        // Precompute inheritance info
                        var inheritanceInfos = new CommandBufferInheritanceInfo[_subPasses.Length];
                        for (int i = 0; i < _subPasses.Length; i++)
                        {
                            inheritanceInfos[i] = new CommandBufferInheritanceInfo
                            {
                                SType = StructureType.CommandBufferInheritanceInfo,
                                RenderPass = camera.RenderTarget.RenderPass,
                                Subpass = (uint)i,
                                Framebuffer = camera.RenderTarget.Framebuffers[frameIndex],
                                OcclusionQueryEnable = false,
                                QueryFlags = QueryControlFlags.None,
                                PipelineStatistics = QueryPipelineStatisticFlags.None
                            };
                        }

                        // Record subpasses in parallel
                        var secondaryBatches = new UploadBatch[_subPasses.Length];
                        var recordingTasks = new Task[_subPasses.Length];

                        for (int i = 0; i < _subPasses.Length; i++)
                        {
                            int subpassIndex = i;
                            recordingTasks[subpassIndex] = Task.Run(() =>
                            {
                                var secondaryBatch = submitContext.CreateBatch(new BatchCreationParams
                                {
                                    Level = CommandBufferLevel.Secondary,
                                    InheritanceInfo = inheritanceInfos[subpassIndex],
                                });
                                secondaryBatches[subpassIndex] = secondaryBatch;

                                // Only record if the batch is invalid or one-time submit
                                RecordSubpassCommand(secondaryBatch, subpassIndex, camera, camIndex, frameIndex);
                            });
                        }

                        // TODO: MAKE IT FULLY ASYNC, CURRENTLY WE ARE BLOCKING THREAD BY THAT
                        // Wait for all recordings to complete
                        Task.WaitAll(recordingTasks);

                        // Execute secondary command buffers
                        for (int i = 0; i < secondaryBatches.Length; i++)
                        {
                            primaryBatch.ExecuteCommands(secondaryBatches[i]);
                            if (i < _subPasses.Length - 1)
                            {
                                cmd.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                            }
                        }

                        cmd.EndRenderPass();
                        camera.RenderTarget.TransitionToRead(cmd);
                    }
                }

                primaryBatch.Submit();
                return Task.CompletedTask;
            }
        }

        private void RecordSubpassCommand(
            UploadBatch batch,
            int subpassIndex,
            Camera camera,
            int camIndex,
            uint frameIndex)
        {
            var cmd = batch.CommandBuffer;
            var subpass = _subPasses[subpassIndex];
            // Execute the subpass
            using (PerformanceTracer.BeginSection(subpass.GetMetadata().Name, cmd, frameIndex))
            {
                subpass.Execute(cmd, frameIndex, camera, camIndex);
            }

            // End the batch after recording
            batch.End();
        }

        private unsafe static void BeginRenderPass(Camera camera, WorldRenderer renderer, VkCommandBuffer cmd)
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
    }
}