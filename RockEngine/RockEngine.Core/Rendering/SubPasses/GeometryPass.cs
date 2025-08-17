using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.SubPasses
{
    public class GeometryPass(
        VulkanContext context,
        GraphicsEngine graphicsEngine,
        BindingManager bindingManager,
        TransformManager transformManager,
        IndirectCommandManager indirectCommands,
        GlobalUbo globalUbo) : IRenderSubPass
    {
        private readonly VulkanContext _context = context;
        private readonly GraphicsEngine _graphicsEngine = graphicsEngine;
        private readonly BindingManager _bindingManager = bindingManager;
        private readonly TransformManager _transformManager = transformManager;
        private readonly IndirectCommandManager _indirectCommands = indirectCommands;
        private readonly GlobalUbo _globalUbo = globalUbo;

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
                var globalUbo = _globalUbo.GetBinding((uint)camIndex);

                // Get draw groups as span for zero-allocation iteration
                var drawGroupsSpan = CollectionsMarshal.AsSpan(
                    _indirectCommands.GetDrawGroups(RenderLayerType.Opaque, Order));

                // Cache last bound states to minimize state changes
                VkPipeline? lastPipeline = default;
                Material? lastMaterial = default;
                MeshRenderer? lastMesh = default;
                bool multiDraw = GetMultiDrawIndirectFeature();

                unsafe
                {
                    // Use stackalloc for small arrays to avoid heap allocations
                    uint* dynamicOffsets = stackalloc uint[2];
                    dynamicOffsets[0] = 0;
                    dynamicOffsets[1] = 0;

                    // Iterate using for loop with ref local to avoid struct copies
                    for (int i = 0; i < drawGroupsSpan.Length; i++)
                    {
                        ref readonly var drawGroup = ref drawGroupsSpan[i];

                        // Pipeline state change
                        if (lastPipeline != drawGroup.Pipeline)
                        {
                            cmd.BindPipeline(drawGroup.Pipeline);
                            lastPipeline = drawGroup.Pipeline;

                            // Bind global resources
                            _bindingManager.BindResource(frameIndex, globalUbo, cmd, drawGroup.Pipeline.Layout);
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
                                [matrixBinding.SetLocation, globalUbo.SetLocation]
                            );
                            lastMaterial = drawGroup.Material;
                        }

                        // Mesh change
                        if (lastMesh != drawGroup.Mesh)
                        {
                            drawGroup.Mesh.Mesh.VertexBuffer.BindVertexBuffer(cmd);
                            drawGroup.Mesh.Mesh.IndexBuffer.BindIndexBuffer(cmd, 0, IndexType.Uint32);
                            lastMesh = drawGroup.Mesh;
                        }

                        // Issue draw command
                        if (multiDraw)
                        {
                            VulkanContext.Vk.CmdDrawIndexedIndirect(
                                cmd,
                                _indirectCommands.IndirectBuffer.Buffer,
                                drawGroup.ByteOffset,
                                drawGroup.Count,
                                (uint)Marshal.SizeOf<DrawIndexedIndirectCommand>());
                        }
                        else
                        {
                            // Pre-calculate stride
                            int stride = Marshal.SizeOf<DrawIndexedIndirectCommand>();

                            for (uint j = 0; j < drawGroup.Count; j++)
                            {
                                VulkanContext.Vk.CmdDrawIndexedIndirect(
                                    cmd,
                                    _indirectCommands.IndirectBuffer.Buffer,
                                    drawGroup.ByteOffset + (ulong)(j * stride),
                                    1,
                                    (uint)stride);
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
