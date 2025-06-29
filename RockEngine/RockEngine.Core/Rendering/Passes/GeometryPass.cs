using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Passes
{
    public class GeometryPass : Subpass
    {
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly GlobalUbo _globalUbo;
        protected override uint Order => 0;

        public GeometryPass(
            VulkanContext context,
            BindingManager bindingManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo)
            : base(context, bindingManager)
        {
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _globalUbo = globalUbo;
        }

        public override async Task Execute(VkCommandBuffer cmd, params object[] args)
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
                    

                    BindingManager.BindResource(frameIndex, _globalUbo.GetBinding((uint)camIndex) , cmd, drawGroup.Pipeline.Layout);
                    BindingManager.BindResource(frameIndex, matrixBinding, cmd, drawGroup.Pipeline.Layout);
                }

                BindingManager.BindResourcesForMaterial(frameIndex, drawGroup.Mesh.Material, cmd, false,[matrixBinding.SetLocation, _globalUbo.GetBinding((uint)camIndex).SetLocation]);

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
        }

        private Silk.NET.Core.Bool32 GetMultiDrawIndirectFeature()
        {
            return Context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
        }
    }
}
