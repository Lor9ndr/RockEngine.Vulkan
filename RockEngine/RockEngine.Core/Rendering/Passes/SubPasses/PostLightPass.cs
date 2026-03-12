using RockEngine.Core.Builders;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Passes.SubPasses
{
    public class PostLightPass : IRenderSubPass
    {
        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly GlobalUbo _globalUbo;
        private readonly GlobalGeometryBuffer _geometryBufferManager;
        private readonly Bool32 _supportsMultiDraw;
        private readonly int _indirectCommandStride;

        public static uint Order => 2;

        public static string Name => "forward";

        public PostLightPass(VulkanContext context,
            BindingManager bindingManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo,
            GlobalGeometryBuffer geometryBufferManager)
        {
            _context = context;
            _bindingManager = bindingManager;
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _globalUbo = globalUbo;
            _geometryBufferManager = geometryBufferManager;
            _supportsMultiDraw = GetMultiDrawIndirectFeature();
            _indirectCommandStride = Marshal.SizeOf<DrawIndexedIndirectCommand>();
        }
        public void Initilize()
        {
        }

        public SubPassMetadata GetMetadata()
        {
            return new(Order, Name);
        }
        public void Execute(UploadBatch batch, params object[] args)
        {
            using (PerformanceTracer.BeginSection(nameof(PostLightPass)))
            {

                uint frameIndex = (uint)args[0];
                var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));
                var camIndex = (int)args[2];
                batch.LabelObject(Name);
                batch.SetViewport(camera.RenderTarget.Viewport);
                batch.SetScissor(camera.RenderTarget.Scissor);

                var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
                var globalUboBinding = _globalUbo.GetBinding((uint)camIndex);

                var drawGroups = _indirectCommands.GetDrawGroups<PostLightPass>();
                if (drawGroups.Count == 0)
                {
                    return;
                }

                // Get the span of draw groups
                var drawGroupsSpan = CollectionsMarshal.AsSpan(drawGroups);

                // Pre-calculate common values
                var indirectBuffer = _indirectCommands.IndirectBuffer.Buffer;

                // Track state
                RckPipeline? lastPipeline = null;
                MaterialPass? lastMaterialPass = null;

                // Привязываем общие буферы вершин и индексов
                _geometryBufferManager.Bind(batch);

                for (int i = 0; i < drawGroupsSpan.Length; i++)
                {
                    ref readonly var drawGroup = ref drawGroupsSpan[i];
                    if (!camera.CanRender(drawGroup.MeshRenderer.Entity))
                    {
                        continue;
                    }

                    // Pipeline state change
                    if (lastPipeline != drawGroup.MaterialPass.Pipeline)
                    {
                        batch.BindPipeline(drawGroup.MaterialPass.Pipeline);
                        lastPipeline = drawGroup.MaterialPass.Pipeline;

                        // Bind global resources using the binding manager
                        _bindingManager.BindResource(frameIndex, globalUboBinding, batch, drawGroup.MaterialPass.Pipeline.Layout);
                        _bindingManager.BindResource(frameIndex, matrixBinding, batch, drawGroup.MaterialPass.Pipeline.Layout);
                    }

                    // Material change
                    if (lastMaterialPass != drawGroup.MaterialPass)
                    {
                        _bindingManager.BindResourcesForMaterial(
                            frameIndex,
                            drawGroup.MaterialPass,
                            batch,
                            false,
                            [matrixBinding.SetLocation, globalUboBinding.SetLocation]
                        );
                        lastMaterialPass = drawGroup.MaterialPass;
                        lastMaterialPass.CmdPushConstants(batch);
                    }
                    

                    // Issue draw command
                    if (drawGroup.IsMultiDraw && _supportsMultiDraw)
                    {
                        batch.DrawIndexedIndirect(
                            indirectBuffer,
                            drawGroup.Count,
                            drawGroup.ByteOffset,
                            (uint)_indirectCommandStride);
                    }
                    else
                    {
                        for (uint j = 0; j < drawGroup.Count; j++)
                        {
                            batch.DrawIndexedIndirect(
                                indirectBuffer,
                                1,
                                drawGroup.ByteOffset + (ulong)(j * _indirectCommandStride),
                                (uint)_indirectCommandStride);
                        }
                    }
                }

            }
        }


        private Bool32 GetMultiDrawIndirectFeature()
        {
            return _context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
        }


        public void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass)
        {
            int colorIndex = GBuffer.ColorAttachmentFormats.Length + 1;
            int depthIndex = GBuffer.ColorAttachmentFormats.Length;

            subpass.AddColorAttachment(colorIndex, ImageLayout.ColorAttachmentOptimal);
            subpass.SetDepthAttachment(depthIndex, ImageLayout.DepthStencilReadOnlyOptimal);
        }

        public void SetupDependencies(RenderPassBuilder builder, uint subpassIndex)
        {
            // LightingPass -> PostLightPass dependency
            if (subpassIndex == 2)
            {
                builder.AddDependency()
                    .FromSubpass(subpassIndex - 1)
                    .ToSubpass(subpassIndex)
                    .WithStages(
                        PipelineStageFlags.ColorAttachmentOutputBit,
                        PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit)
                    .WithAccess(
                        AccessFlags.ColorAttachmentWriteBit,
                        AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit)
                    .Add();
            }
        }
        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
        }

        public void Dispose()
        {
        }

       
    }
}
