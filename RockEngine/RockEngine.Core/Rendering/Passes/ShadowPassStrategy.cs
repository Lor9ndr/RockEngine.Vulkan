using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Core.Rendering.Passes
{
    public sealed class ShadowPassStrategy(VulkanContext context,
                                           LightManager lightManager,
                                           ShadowManager shadowManager,
                                           IEnumerable<IRenderSubPass> subPasses) 
        : PassStrategyBase(context, subPasses), IDisposable
    {
        private readonly ConcurrentDictionary<Light, ShadowRenderTarget> _shadowTargets = new();
        private bool _disposed;

        public override int Order => -10000;
        private static readonly float[] _shadowPassColors = [0.2f, 0.2f, 0.2f, 1.0f];

        public override async ValueTask Execute(SubmitContext submitContext, CameraManager cameraManager, WorldRenderer renderer)
        {
            var shadowCastingLights = lightManager.GetShadowCastingLights().Take(3);
            var lst = shadowCastingLights.ToList();
            var mainCamera = cameraManager.RegisteredCameras.FirstOrDefault();
            if (mainCamera == null)
            {
                return;
            }
            shadowManager.UpdateShadowMatrices(lst, mainCamera);
            // Batch point lights for better GPU utilization

            var batchSize = Math.Min(2, lst.Count);
            for (int i = 0; i < lst.Count; i += batchSize)
            {
                var currentBatch = Math.Min(batchSize, lst.Count - i);
                var tasks = new Task[currentBatch];

                for (int j = 0; j < currentBatch; j++)
                {
                    var light = lst[i + j];
                    tasks[j] = RenderShadowMap(submitContext, light, renderer);
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        private async Task RenderShadowMap(SubmitContext submitContext, Light light, WorldRenderer renderer)
        {
            using var tracer = PerformanceTracer.BeginSection($"Shadow Pass - {light.Entity.Name}");

            var primaryBatch = submitContext.CreateBatch();

            using (PerformanceTracer.BeginSection($"ShadowMap_{light.Entity.Name}", primaryBatch, renderer.FrameIndex))
            {
                var shadowTarget = GetOrCreateShadowTarget(light);

                using (primaryBatch.NameAction($"ShadowMap_{light.Entity.Name}", _shadowPassColors))
                {
                    // Pre-calculate viewport and scissor once
                    var viewport = new Viewport(0, 0, light.ShadowMapSize, light.ShadowMapSize, 0, 1);
                    var scissor = new Rect2D(new Offset2D(), new Extent2D(light.ShadowMapSize, light.ShadowMapSize));

                    BeginShadowRenderPass(primaryBatch, shadowTarget);
                    primaryBatch.SetViewport(viewport);
                    primaryBatch.SetScissor(scissor);

                    // Execute subpass directly without virtual call overhead
                    _subPasses[0].Execute(primaryBatch, renderer.FrameIndex, light);

                    primaryBatch.EndRenderPass();
                }

                shadowManager.UpdateShadowTexture(primaryBatch, light, shadowTarget.Image);
            }

            
            primaryBatch.Submit();
        }
        private ShadowRenderTarget GetOrCreateShadowTarget(Light light)
        {
            return _shadowTargets.GetOrAdd(light, static (l, ctx) =>
            {
                var (context, renderPass) = ctx;
                var newTarget = new ShadowRenderTarget(context, l);
                newTarget.Initialize(renderPass!);
                return newTarget;
            }, (_context, RenderPass));
        }

        public override void Dispose()
        {
            if (_disposed) return;

            foreach (var (_, target) in _shadowTargets)
            {
                target.Dispose();
            }
            _shadowTargets.Clear();

            base.Dispose();
            _disposed = true;
        }
        private unsafe void BeginShadowRenderPass(UploadBatch batch, ShadowRenderTarget shadowTarget)
        {
            Span<ClearValue> clearValues = stackalloc ClearValue[shadowTarget.ClearValues.Length];
            shadowTarget.ClearValues.CopyTo(clearValues);

            fixed (ClearValue* pClearValues = clearValues)
            {
                var renderPassBeginInfo = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = shadowTarget.RenderPass,
                    Framebuffer = shadowTarget.Framebuffers[0],
                    RenderArea = shadowTarget.Scissor,
                    ClearValueCount = (uint)clearValues.Length,
                    PClearValues = pClearValues
                };

                batch.BeginRenderPass(in renderPassBeginInfo, SubpassContents.Inline);
            }
        }
    }
}