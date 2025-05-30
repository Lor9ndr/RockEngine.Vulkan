﻿using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Passes
{
    public class LightingPass : Subpass
    {
        private readonly LightManager _lightManager;
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly VkPipeline _lightingPipeline;
        private readonly GlobalUbo _globalUbo;
        private readonly UniformBufferBinding _binding;

        public LightingPass(
            VulkanContext context,
            BindingManager bindingManager,
            LightManager lightManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo,
            VkPipeline lightingPipeline)
            : base(context, bindingManager)
        {
            _lightManager = lightManager;
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _lightingPipeline = lightingPipeline;
            _globalUbo = globalUbo;
            _binding = new UniformBufferBinding(_globalUbo, 0, 0);
        }

        protected override uint Order => 1;

        public override async Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            var camera = args[0] as Camera ?? throw new ArgumentNullException(nameof(Camera));
            uint frameIndex = (uint)args[1];

            cmd.SetViewport(camera.RenderTarget.Viewport);
            cmd.SetScissor(camera.RenderTarget.Scissor);
            // 2. Draw skybox/transparent objects afterward
            foreach (var drawGroup in _indirectCommands.GetDrawGroups(RenderLayerType.Transparent))
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

            // 1. Draw the lighting quad first
            cmd.BindPipeline(_lightingPipeline, PipelineBindPoint.Graphics);
            BindingManager.BindResourcesForMaterial(camera.RenderTarget.GBuffer.Material, cmd);
            cmd.Draw(3, 1, 0, 0);

         
        }

        private Silk.NET.Core.Bool32 GetMultiDrawIndirectFeature()
        {
            return Context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
        }
    }
}
