using RockEngine.Core.Diagnostics;
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

        public LightingPass LightingPass => SubPasses.OfType<LightingPass>().First();
        public override int Order => 0;

        public DeferredPassStrategy(
             VulkanContext context,
             IEnumerable<IRenderSubPass> subpasses,
             GlobalUbo globalUbo)
             : base(context, subpasses)
        {
            _globalUbo = globalUbo;

        }

        public override async ValueTask Execute(SubmitContext submitContext, CameraManager cameraManager, WorldRenderer renderer)
        {
            uint frameIndex = renderer.FrameIndex;
            var cams = cameraManager.RegisteredCameras;

            await Parallel.ForAsync(0, cams.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, async(i, ct) =>
            {
                Camera camera = cams[i];
                if (camera.RenderTarget.RenderPass != RenderPass)
                {
                    return;
                }
                if (camera.IsActive)
                {
                   await ExecuteCameraPass(submitContext, camera, renderer, i, frameIndex);
                }
            });
           
        }


        private ValueTask ExecuteCameraPass(SubmitContext submitContext, Camera camera, WorldRenderer renderer, int camIndex, uint frameIndex)
        {

            var name = $"Camera - {camera.Entity.Name}";
            using (PerformanceTracer.BeginSection(name))
            {
                var primaryBatch = submitContext.CreateBatch();
                var batch = primaryBatch;
                using (batch.NameAction(name, [0.5f, 0.8f, 0.9f, 1.0f]))
                {
                    using (PerformanceTracer.BeginSection(name, batch, frameIndex))
                    {
                        camera.RenderTarget.PrepareForRender(primaryBatch);
                        BeginRenderPass(camera, renderer, batch);

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
                        Task[] recordingTasks = new Task[_subPasses.Length];

                        for (int i = 0; i < _subPasses.Length; i++)
                        {
                            int subpassIndex = i;

                            recordingTasks[i] = Task.Run(() =>
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
                        
                        Task.WhenAll(recordingTasks).GetAwaiter().GetResult();


                        // Execute secondary command buffers
                        for (int i = 0; i < secondaryBatches.Length; i++)
                        {
                            primaryBatch.ExecuteCommands(secondaryBatches[i]);
                            if (i < _subPasses.Length - 1)
                            {
                                batch.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                            }
                        }

                        batch.EndRenderPass();
                        camera.RenderTarget.TransitionToRead(primaryBatch);
                    }
                }

                primaryBatch.Submit();
                return ValueTask.CompletedTask;
            }
        }

        private void RecordSubpassCommand(
            UploadBatch batch,
            int subpassIndex,
            Camera camera,
            int camIndex,
            uint frameIndex)
        {
            var subpass = _subPasses[subpassIndex];
            // Execute the subpass
            using (PerformanceTracer.BeginSection(subpass.GetMetadata().Name, batch, frameIndex))
            {
                subpass.Execute(batch, frameIndex, camera, camIndex);
            }

            // End the batch after recording
            batch.End();
        }

        private unsafe static void BeginRenderPass(Camera camera, WorldRenderer renderer, UploadBatch batch)
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

                batch.BeginRenderPass(in renderPassBeginInfo, SubpassContents.SecondaryCommandBuffers);
            }
        }
    }
}