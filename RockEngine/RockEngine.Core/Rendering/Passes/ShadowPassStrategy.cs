using RockEngine.Core.Diagnostics;
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
                                           GraphicsContext graphicsContext,
                                           CameraManager cameraManager,
                                           IEnumerable<IRenderSubPass> subPasses) 
        : PassStrategyBase(context, subPasses), IDisposable
    {
        private readonly ConcurrentDictionary<Light, ShadowRenderTarget> _shadowTargets = new();
        private bool _disposed;

        public override int Order => -10000;
        private static readonly float[] _shadowPassColors = [0.2f, 0.2f, 0.2f, 1.0f];

        public override async ValueTask Execute(RenderContext renderContext, WorldRenderer renderer)
        {
            var shadowCastingLights = lightManager.GetShadowCastingLights();
            var lst = shadowCastingLights.ToList();
            var mainCamera = cameraManager.RegisteredCameras.Count == 0 ? default : cameraManager.RegisteredCameras[0];
            if (mainCamera == null || lst.Count == 0)
            {
                return;
            }
            shadowManager.UpdateShadowMatrices(lst, mainCamera);
            // Batch point lights for better GPU utilization
            var primaryBatch = renderContext.GraphicsContext.CreateBatch();

            for (int i = 0; i < lst.Count; i++)
            {
                Light? light = lst[i];
                await RenderShadowMap(primaryBatch, renderContext.GraphicsContext, light,renderer, i);
            }
            primaryBatch.Submit();

        }

        private async Task RenderShadowMap(UploadBatch batch, SubmitContext submitContext, Light light, WorldRenderer renderer, int lightIndex)
        {
            using var tracer = PerformanceTracer.BeginSection($"Shadow Pass - {light.Entity.Name}");


            using (PerformanceTracer.BeginSection($"ShadowMap_{light.Entity.Name}", batch, renderer.FrameIndex))
            {
                var shadowTarget = GetOrCreateShadowTarget(light);

                using (batch.NameAction($"ShadowMap_{light.Entity.Name}", _shadowPassColors))
                {
                    // Pre-calculate viewport and scissor once
                    var viewport = new Viewport(0, 0, light.ShadowMapSize, light.ShadowMapSize, 0, 1);
                    var scissor = new Rect2D(new Offset2D(), new Extent2D(light.ShadowMapSize, light.ShadowMapSize));

                    BeginShadowRenderPass(batch, shadowTarget);
                    batch.SetViewport(viewport);
                    batch.SetScissor(scissor);
                    //using (BeginQueryScope(batch, renderer.FrameIndex, (uint)lightIndex, 0u))
                    {
                        // Execute subpass directly without virtual call overhead
                        _subPasses[0].Execute(batch, renderer.FrameIndex, light);
                    }

                    batch.EndRenderPass();
                }
                shadowManager.UpdateShadowTexture(batch, light, shadowTarget.Image);
            }

        }
        private ShadowRenderTarget GetOrCreateShadowTarget(Light light)
        {
            var renderTarget = _shadowTargets.GetOrAdd(light, static (l, ctx) =>
            {
                var (context, renderPass) = ctx;
                var newTarget = new ShadowRenderTarget(context, l);
                newTarget.Initialize(renderPass!);
                newTarget.Image.LabelObject($"ShadowRenderTarget ({l.Entity.Name})");
                return newTarget;
            }, (_context, RenderPass));
            if (renderTarget.LightType != light.Type)
            {
                _shadowTargets.TryRemove(light, out _);
                renderTarget = _shadowTargets.GetOrAdd(light, static (l, ctx) =>
                {
                    var (context, renderPass) = ctx;
                    var newTarget = new ShadowRenderTarget(context, l);
                    newTarget.Initialize(renderPass!);
                    newTarget.Image.LabelObject($"ShadowRenderTarget ({l.Entity.Name})");
                    return newTarget;
                }, (_context, RenderPass));
            }
            return renderTarget;
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

        public override ValueTask Update()
        {
            //RetrievePipelineStatistics(graphicsContext.FrameIndex);
            return ValueTask.CompletedTask;
        }
        private unsafe void BeginShadowRenderPass(UploadBatch batch, ShadowRenderTarget shadowTarget)
        {
            Span<ClearValue> clearValues = stackalloc ClearValue[shadowTarget.ClearValues.Length];
            shadowTarget.ClearValues.Span.CopyTo(clearValues);

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