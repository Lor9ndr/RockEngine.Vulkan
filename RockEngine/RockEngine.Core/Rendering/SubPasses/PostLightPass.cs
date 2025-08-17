using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

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
        public uint Order => 2;

        public PostLightPass(VulkanContext context,
            BindingManager bindingManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo)
        {
            _context = context;
            _bindingManager = bindingManager;
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _globalUbo = globalUbo;
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

                // Get draw groups as span
                var drawGroupsSpan = CollectionsMarshal.AsSpan(_indirectCommands.GetDrawGroups(RenderLayerType.Solid, Order));

                // Cache last bound states
                VkPipeline? lastPipeline = default;
                Material? lastMaterial = default;
                MeshRenderer? lastMesh = default;
                bool multiDraw = GetMultiDrawIndirectFeature();
                unsafe
                {
                    // Stackalloc for dynamic offsets
                    uint* dynamicOffsets = stackalloc uint[2];
                    dynamicOffsets[0] = 0;
                    dynamicOffsets[1] = 0;

                    for (int i = 0; i < drawGroupsSpan.Length; i++)
                    {
                        ref readonly var drawGroup = ref drawGroupsSpan[i];

                        if (lastPipeline != drawGroup.Pipeline)
                        {
                            cmd.BindPipeline(drawGroup.Pipeline, PipelineBindPoint.Graphics);
                            lastPipeline = drawGroup.Pipeline;

                            _bindingManager.BindResource(frameIndex, globalUboBinding, cmd, drawGroup.Pipeline.Layout);
                            _bindingManager.BindResource(frameIndex, matrixBinding, cmd, drawGroup.Pipeline.Layout);
                        }

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
                        }

                        lastMaterial?.CmdPushConstants(cmd);

                        if (lastMesh != drawGroup.Mesh)
                        {
                            drawGroup.Mesh.Mesh.VertexBuffer.BindVertexBuffer(cmd);
                            drawGroup.Mesh.Mesh.IndexBuffer.BindIndexBuffer(cmd, 0, IndexType.Uint32);
                            lastMesh = drawGroup.Mesh;
                        }

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
