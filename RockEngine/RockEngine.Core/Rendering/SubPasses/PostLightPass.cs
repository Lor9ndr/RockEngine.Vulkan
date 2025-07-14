using RockEngine.Core.Builders;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes;
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


        public Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            uint frameIndex = (uint)args[0];
            var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));
            var camIndex = (int)args[2];
            cmd.SetViewport(camera.RenderTarget.Viewport);
            cmd.SetScissor(camera.RenderTarget.Scissor);
            var pipeline = default(VkPipeline);
            foreach (var drawGroup in _indirectCommands.GetDrawGroups(RenderLayerType.Solid))
            {
                if (drawGroup.Pipeline.SubPass != Order)
                {
                    continue;
                }
                if(drawGroup.Pipeline != pipeline)
                {
                    cmd.BindPipeline(drawGroup.Pipeline, PipelineBindPoint.Graphics);
                    pipeline = drawGroup.Pipeline;
                    var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);

                    _bindingManager.BindResource(frameIndex, _globalUbo.GetBinding((uint)camIndex), cmd, drawGroup.Pipeline.Layout);
                    _bindingManager.BindResource(frameIndex, matrixBinding, cmd, drawGroup.Pipeline.Layout);
                }

                _bindingManager.BindResourcesForMaterial(frameIndex,drawGroup.Mesh.Material, cmd);

                drawGroup.Mesh.Material.CmdPushConstants(cmd);

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
