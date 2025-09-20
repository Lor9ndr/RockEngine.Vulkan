using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.SubPasses
{
    public class GeometryPass : IRenderSubPass
    {
        private readonly VulkanContext _context;
        private readonly GraphicsEngine _graphicsEngine;
        private readonly BindingManager _bindingManager;
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly GlobalUbo _globalUbo;
        private readonly GlobalGeometryBuffer _globalGeometryBuffer;
        private readonly Bool32 _supportsMultiDraw;
        private readonly int _indirectCommandStride;

        public GeometryPass(
            VulkanContext context,
            GraphicsEngine graphicsEngine,
            BindingManager bindingManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo, GlobalGeometryBuffer globalGeometryBuffer)
        {
            _context = context;
            _graphicsEngine = graphicsEngine;
            _bindingManager = bindingManager;
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _globalUbo = globalUbo;
            _globalGeometryBuffer = globalGeometryBuffer;
            _supportsMultiDraw = GetMultiDrawIndirectFeature();
            _indirectCommandStride = Marshal.SizeOf<DrawIndexedIndirectCommand>();
        }

        public uint Order => 0;


        public void Execute(VkCommandBuffer cmd, params object[] args)
        {
            using (PerformanceTracer.BeginSection(nameof(GeometryPass)))
            {
                uint frameIndex = (uint)args[0];
                var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));
                var camIndex = (int)args[2];

                cmd.SetViewport(camera.RenderTarget.Viewport);
                cmd.SetScissor(camera.RenderTarget.Scissor);

                var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
                var globalUboBinding = _globalUbo.GetBinding((uint)camIndex);


                var drawGroups = _indirectCommands.GetDrawGroups(RenderLayerType.Opaque, Order);
                if (drawGroups.Count == 0) return;

                // Get the span of draw groups
                var drawGroupsSpan = CollectionsMarshal.AsSpan(drawGroups);

                // Pre-calculate common values
                var indirectBuffer = _indirectCommands.IndirectBuffer.Buffer;

                // Track state
                VkPipeline? lastPipeline = null;
                Material? lastMaterial = null;
                _globalGeometryBuffer.BindVertexBuffer(cmd);
                _globalGeometryBuffer.BindIndexBuffer(cmd);

                unsafe
                {
                    // Use stackalloc for dynamic offsets
                    uint* dynamicOffsets = stackalloc uint[2];
                    dynamicOffsets[0] = 0;
                    dynamicOffsets[1] = 0;

                    for (int i = 0; i < drawGroupsSpan.Length; i++)
                    {
                        ref readonly var drawGroup = ref drawGroupsSpan[i];

                        // Pipeline state change
                        if (lastPipeline != drawGroup.Pipeline)
                        {
                            cmd.BindPipeline(drawGroup.Pipeline);
                            lastPipeline = drawGroup.Pipeline;

                            // Bind global resources using the binding manager
                            _bindingManager.BindResource(frameIndex, globalUboBinding, cmd, drawGroup.Pipeline.Layout);
                            _bindingManager.BindResource(frameIndex, matrixBinding, cmd, drawGroup.Pipeline.Layout);
                        }

                        // Material change
                        if (lastMaterial != drawGroup.Material)
                        {
                            _bindingManager.BindResourcesForMaterial(
                                frameIndex,
                                drawGroup.Material,
                                cmd,
                                false,
                                [matrixBinding.SetLocation, globalUboBinding.SetLocation]
                            );
                            lastMaterial = drawGroup.Material;
                            lastMaterial.CmdPushConstants(cmd);
                        }

                        // Issue draw command
                        if (_supportsMultiDraw)
                        {
                            VulkanContext.Vk.CmdDrawIndexedIndirect(
                                cmd,
                                indirectBuffer,
                                drawGroup.ByteOffset,
                                drawGroup.Count,
                                (uint)_indirectCommandStride);
                        }
                        else
                        {
                            for (uint j = 0; j < drawGroup.Count; j++)
                            {
                                VulkanContext.Vk.CmdDrawIndexedIndirect(
                                    cmd,
                                    indirectBuffer,
                                    drawGroup.ByteOffset + (ulong)(j * _indirectCommandStride),
                                    1,
                                    (uint)_indirectCommandStride);
                            }
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Silk.NET.Core.Bool32 GetMultiDrawIndirectFeature()
        {
            return _context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
        }
        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
            // GBuffer Color Attachments
            for (int i = 0; i < GBuffer.ColorAttachmentFormats.Length; i++)
            {
                builder.ConfigureAttachment(GBuffer.ColorAttachmentFormats[i])
                    .WithColorOperations(
                        load: AttachmentLoadOp.Clear,
                        store: AttachmentStoreOp.Store,
                        initialLayout: ImageLayout.Undefined,
                        finalLayout: ImageLayout.ShaderReadOnlyOptimal)
                    .Add();
            }

            // Depth Attachment
            builder.ConfigureAttachment(_graphicsEngine.Swapchain.DepthFormat)
                .WithDepthOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.DontCare,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.DepthStencilReadOnlyOptimal)
                .Add();
        }

        public void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass)
        {
            // Color attachments (GBuffer)
            for (int i = 0; i < GBuffer.ColorAttachmentFormats.Length; i++)
            {
                subpass.AddColorAttachment(i, ImageLayout.ColorAttachmentOptimal);
            }

            // Depth attachment
            subpass.SetDepthAttachment(
                GBuffer.ColorAttachmentFormats.Length,
                ImageLayout.DepthStencilAttachmentOptimal);
        }

        public void SetupDependencies(RenderPassBuilder builder, uint subpassIndex)
        {
            // External -> GeometryPass dependency
            if (subpassIndex == 0)
            {
                builder.AddDependency()
                    .FromExternal()
                    .ToSubpass(subpassIndex)
                    .WithStages(
                        PipelineStageFlags.BottomOfPipeBit,
                        PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit)
                    .WithAccess(
                        AccessFlags.None,
                        AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit)
                    .Add();
            }
        }

        public void Dispose()
        {
        }

        public void Initilize()
        {
        }
    }
}
