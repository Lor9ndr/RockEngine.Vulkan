using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Core.Rendering.Passes
{
    public class DeferredPassStrategy : PassStrategyBase
    {
        private readonly GlobalUbo _globalUbo;
        private readonly ThreadLocal<CommandBufferPool> _commandBufferPool;
        private readonly ConcurrentBag<VkCommandBuffer[]>[] _frameBufferArrays;

        public LightingPass LightingPass => SubPasses.OfType<LightingPass>().First();
        public override int Order => int.MinValue;

        public DeferredPassStrategy(
            VulkanContext context,
            IEnumerable<IRenderSubPass> subpasses,
            GlobalUbo globalUbo)
            : base(context, subpasses)
        {
            _globalUbo = globalUbo;
            _commandBufferPool = new ThreadLocal<CommandBufferPool>(()=>
            {
                return new CommandBufferPool(
                context,
                CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit,
                context.Device.GraphicsQueue.FamilyIndex
            );

            });

            // Create per-frame buffer arrays
            _frameBufferArrays = new ConcurrentBag<VkCommandBuffer[]>[_context.MaxFramesPerFlight];
            for (int i = 0; i < _context.MaxFramesPerFlight; i++)
            {
                _frameBufferArrays[i] = new ConcurrentBag<VkCommandBuffer[]>();
            }
        }

        public override void Execute(UploadBatch recorder, CameraManager cameraManager, Renderer renderer)
        {
            int frameIndex = (int)renderer.FrameIndex;
            //var tasks = new Task[cameraManager.ActiveCameras.Count];

            for (int i = 0; i < cameraManager.ActiveCameras.Count; i++)
            {
                Camera camera = cameraManager.ActiveCameras[i];
                ExecuteCameraPass(recorder, camera, renderer, i, frameIndex);
            }
        }

        private void ExecuteCameraPass(
           UploadBatch recorder,
           Camera camera,
           Renderer renderer,
           int camIndex,
           int frameIndex)
        {
            using (PerformanceTracer.BeginSection($"Camera {camIndex}"))
            {
                var cmd = recorder.CommandBuffer;
                camera.RenderTarget.PrepareForRender(cmd);
                BeginRenderPass(camera, renderer, cmd);

                // Get or create secondary buffers for this frame
                if (!_frameBufferArrays[frameIndex].TryTake(out VkCommandBuffer[] subpassBuffers))
                {
                    subpassBuffers = new VkCommandBuffer[_subPasses.Length];
                }

                // Create tasks for parallel recording
                var tasks = new Task[_subPasses.Length];
                for (int i = 0; i < _subPasses.Length; i++)
                {
                    int subpassIndex = i;
                    tasks[i] = Task.Run(() =>
                    {
                        ref VkCommandBuffer buffer = ref subpassBuffers[subpassIndex];
                        RecordSubpassCommand(subpassIndex, camera, renderer, camIndex, ref buffer);
                    });
                }

                // Wait for all recordings to complete
                Task.WaitAll(tasks);

                // Execute secondary buffers
                for (int i = 0; i < subpassBuffers.Length; i++)
                {
                    cmd.ExecuteSecondary(subpassBuffers[i]);
                    if (i < subpassBuffers.Length - 1)
                    {
                        cmd.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                    }
                }

                cmd.EndRenderPass();
                camera.RenderTarget.TransitionToRead(cmd);


                // Return buffers to pool after frame completes
                recorder.AddDependency(() =>
                {
                    foreach (VkCommandBuffer buffer in subpassBuffers)
                    {
                        if (buffer != null && !buffer.IsDisposed)
                        {
                            _commandBufferPool.Value.Return(buffer);
                        }
                    }
                    // Store the array for reuse
                    _frameBufferArrays[frameIndex].Add(subpassBuffers);
                });
            }
        }

        private void RecordSubpassCommand(
            int subpassIndex,
            Camera camera,
            Renderer renderer,
            int camIndex,
            ref VkCommandBuffer buffer)
        {
            // Get a buffer from the pool if needed
            if (buffer == null || buffer.IsDisposed)
            {
                buffer = _commandBufferPool.Value.Get(CommandBufferLevel.Secondary);
            }

            var inheritanceInfo = new CommandBufferInheritanceInfo
            {
                SType = StructureType.CommandBufferInheritanceInfo,
                RenderPass = camera.RenderTarget.RenderPass,
                Subpass = (uint)subpassIndex,
                Framebuffer = camera.RenderTarget.Framebuffers[renderer.FrameIndex]
            };

            buffer.Reset(CommandBufferResetFlags.ReleaseResourcesBit);
            buffer.Begin(CommandBufferUsageFlags.RenderPassContinueBit, in inheritanceInfo);

            // Execute subpass synchronously
            _subPasses[subpassIndex].Execute(buffer, renderer.FrameIndex, camera, camIndex);

            buffer.End();
        }

        private static void BeginRenderPass(Camera camera, Renderer renderer, VkCommandBuffer cmd)
        {
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
        }

        public override void Dispose()
        {
            _commandBufferPool.Dispose();
            base.Dispose();
        }
    }
}