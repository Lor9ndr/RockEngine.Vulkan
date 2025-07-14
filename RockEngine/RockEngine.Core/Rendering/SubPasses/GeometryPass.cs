using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

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

      
        public Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            // Extract frame index and camera from args
            uint frameIndex = (uint)args[0];
            var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));
            var camIndex = (int)args[2];
           

            cmd.SetViewport(camera.RenderTarget.Viewport);
            cmd.SetScissor(camera.RenderTarget.Scissor);
            var pipeline = default(VkPipeline);
            foreach (var drawGroup in _indirectCommands.GetDrawGroups(RenderLayerType.Opaque))
            {
                if (drawGroup.Pipeline.SubPass != Order)
                {
                    continue;
                }
                var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
                if (pipeline != drawGroup.Pipeline)
                {
                    cmd.BindPipeline(drawGroup.Pipeline);
                    pipeline = drawGroup.Pipeline;


                    _bindingManager.BindResource(frameIndex, _globalUbo.GetBinding((uint)camIndex) , cmd, drawGroup.Pipeline.Layout);
                    _bindingManager.BindResource(frameIndex, matrixBinding, cmd, drawGroup.Pipeline.Layout);
                }

                _bindingManager.BindResourcesForMaterial(frameIndex, drawGroup.Mesh.Material, cmd, false,[matrixBinding.SetLocation, _globalUbo.GetBinding((uint)camIndex).SetLocation]);

                drawGroup.Mesh.VertexBuffer.BindVertexBuffer(cmd);
                drawGroup.Mesh.IndexBuffer.BindIndexBuffer(cmd, 0, IndexType.Uint32);
                if (GetMultiDrawIndirectFeature())
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
                    for (uint i = 0; i < drawGroup.Count; i++)
                    {
                        VulkanContext.Vk.CmdDrawIndexedIndirect(
                            cmd,
                            _indirectCommands.IndirectBuffer.Buffer,
                            drawGroup.ByteOffset + (ulong)(i * Marshal.SizeOf<DrawIndexedIndirectCommand>()),
                            1,
                            (uint)Marshal.SizeOf<DrawIndexedIndirectCommand>());
                    }
                }
            }
            return Task.CompletedTask;
        }

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
