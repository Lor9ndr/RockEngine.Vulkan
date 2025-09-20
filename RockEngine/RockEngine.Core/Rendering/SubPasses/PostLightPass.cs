using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.SubPasses
{
    internal class PostLightPass : IRenderSubPass
    {
        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly GlobalUbo _globalUbo;
        private readonly GlobalGeometryBuffer _geometryBufferManager;
        private readonly Bool32 _supportsMultiDraw;
        private readonly int _indirectCommandStride;

        public uint Order => 2;

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


        public void Execute(VkCommandBuffer cmd, params object[] args)
        {
            using (PerformanceTracer.BeginSection(nameof(PostLightPass)))
            {
                uint frameIndex = (uint)args[0];
                var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));
                var camIndex = (int)args[2];

                cmd.SetViewport(camera.RenderTarget.Viewport);
                cmd.SetScissor(camera.RenderTarget.Scissor);

              

                var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
                var globalUboBinding = _globalUbo.GetBinding((uint)camIndex);

                var drawGroups = _indirectCommands.GetDrawGroups(RenderLayerType.Solid, Order);
                if (drawGroups.Count == 0) return;

                // Get the span of draw groups
                var drawGroupsSpan = CollectionsMarshal.AsSpan(drawGroups);

                // Pre-calculate common values
                var indirectBuffer = _indirectCommands.IndirectBuffer.Buffer;

                // Track state
                VkPipeline? lastPipeline = null;
                Material? lastMaterial = null;

                // Привязываем общие буферы вершин и индексов
                _geometryBufferManager.BindVertexBuffer(cmd);
                _geometryBufferManager.BindIndexBuffer(cmd);

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


        private Silk.NET.Core.Bool32 GetMultiDrawIndirectFeature()
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
