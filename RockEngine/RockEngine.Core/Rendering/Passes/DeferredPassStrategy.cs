using RockEngine.Core.DI;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Extensions;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Buffers;
using System.Collections.Concurrent;

using ZLinq;

namespace RockEngine.Core.Rendering.Passes
{
    public class DeferredPassStrategy(
         VulkanContext context,
         IEnumerable<IRenderSubPass> subpasses,
         CameraManager cameraManager) : PipelineStatisticsPassStrategyBase(context, subpasses)
    {
        private readonly ConcurrentDictionary<uint, int> _framesInProgress = new();

        public LightingPass LightingPass => SubPasses.OfType<LightingPass>().First();
        public override int Order => 0;

        public override async ValueTask Execute(RenderContext renderContext, WorldRenderer renderer)
        {
            uint frameIndex = renderer.FrameIndex;
            var cams = cameraManager.RegisteredCameras;

            // Increment frame in-progress counter
            _framesInProgress.AddOrUpdate(frameIndex, 1, (_, count) => count + 1);


            await Parallel.ForAsync(0, cams.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, async (i, ct) =>
            {
                Camera camera = cams[i];
                if (camera.RenderTarget.RenderPass != RenderPass)
                {
                    return;
                }
                if (camera.IsActive)
                {
                    await ExecuteCameraPass(renderContext.GraphicsContext, camera, renderer, (uint)i, frameIndex);
                }
            });

            // Decrement frame in-progress counter
            _framesInProgress.AddOrUpdate(frameIndex, 0, (_, count) => count - 1);

            // Clean up old frames
            var framesToRemove = _framesInProgress.AsValueEnumerable()
                .Select(s => s.Key)
                .Where(f => f < frameIndex - 3)
                .ToList();
            foreach (var frame in framesToRemove)
            {
                _framesInProgress.TryRemove(frame, out _);
            }
        }

        private async ValueTask ExecuteCameraPass(SubmitContext submitContext, Camera camera, WorldRenderer renderer, uint cameraIndex, uint frameIndex)
        {
            var name = $"Camera - {camera.Entity.Name}";

            var primaryBatch = submitContext.CreateBatch();
            var batch = primaryBatch;
            using (PerformanceTracer.BeginSection(name) | batch.NameAction(name, [0.5f, 0.8f, 0.9f, 1.0f]) | batch.BeginSection(name, frameIndex))
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
                        PipelineStatistics = PipelineStatisticsEnabled && _pipelineStatsEnabled ?
                            _pipelineStatisticsFlags : QueryPipelineStatisticFlags.None
                    };
                }

                UploadBatch[] secondaryBatches = ArrayPool<UploadBatch>.Shared.Rent(_subPasses.Length);
                try
                {
                    Parallel.For(0, _subPasses.Length,
                   (subpassIndex) =>
                   {
                       var secondaryBatch = submitContext.CreateBatch(new BatchCreationParams
                       {
                           Level = CommandBufferLevel.Secondary,
                           InheritanceInfo = inheritanceInfos[subpassIndex],
                       });
                       secondaryBatches[subpassIndex] = secondaryBatch;

                       using (BeginQueryScope(secondaryBatch, frameIndex, cameraIndex, (uint)subpassIndex))
                       {
                           RecordSubpassCommand(secondaryBatch, subpassIndex, camera, (int)cameraIndex, frameIndex);
                       }

                       secondaryBatch.End();
                   });

                    for (int i = 0; i < _subPasses.Length; i++)
                    {
                        primaryBatch.ExecuteCommands(secondaryBatches[i]);
                        if (i < _subPasses.Length - 1)
                        {
                            batch.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                        }
                    }
                }
                finally
                {
                    ArrayPool<UploadBatch>.Shared.Return(secondaryBatches);
                    batch.EndRenderPass();
                    camera.RenderTarget.TransitionToRead(primaryBatch);
                }
            }

            primaryBatch.Submit();
        }

        private void RecordSubpassCommand(
            UploadBatch batch,
            int subpassIndex,
            Camera camera,
            int camIndex,
            uint frameIndex)
        {
            var subpass = _subPasses[subpassIndex];
            subpass.Execute(batch, frameIndex, camera, camIndex);
        }

        private unsafe static void BeginRenderPass(Camera camera, WorldRenderer renderer, UploadBatch batch)
        {
            fixed (ClearValue* pClearValue = camera.RenderTarget.ClearValues.Span)
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

        public override ValueTask Update()
        {
            var graphicsContext = IoC.Container.GetInstance<GraphicsContext>();
            uint frameIndex = graphicsContext.FrameIndex;

            RetrievePipelineStatistics(frameIndex);

            var batch = _context.GraphicsSubmitContext.CreateBatch();
            BeginFrameQueries(batch, frameIndex);
            batch.Submit();

            return ValueTask.CompletedTask;
        }
    }
}