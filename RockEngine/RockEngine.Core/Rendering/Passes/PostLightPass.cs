using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Passes
{
    internal class PostLightPass : Subpass
    {
        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly GlobalUbo _globalUbo;
        private readonly UniformBufferBinding _binding;
        protected override uint Order => 2;


        public PostLightPass(VulkanContext context,
            BindingManager bindingManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo)
            : base(context, bindingManager)
        {
            _context = context;
            _bindingManager = bindingManager;
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _globalUbo = globalUbo;
            _binding = new UniformBufferBinding(_globalUbo, 0, 0);
        }


        public override Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            uint frameIndex = (uint)args[0];
            var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(Camera));
            cmd.SetViewport(camera.RenderTarget.Viewport);
            cmd.SetScissor(camera.RenderTarget.Scissor);

            foreach (var drawGroup in _indirectCommands.GetDrawGroups(RenderLayerType.Solid))
            {
                if (drawGroup.Pipeline.SubPass != Order)
                {
                    continue;
                }
                cmd.BindPipeline(drawGroup.Pipeline, PipelineBindPoint.Graphics);

                var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
                drawGroup.Mesh.Material.Bindings.Add(matrixBinding);

                drawGroup.Mesh.Material.Bindings.Add(_binding);
                BindingManager.BindResourcesForMaterial(drawGroup.Mesh.Material, cmd);

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
            return Context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
        }
    }
}
