using RockEngine.Core;
using RockEngine.Core.Builders;
using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Editor.Rendering.RenderTargets;
using RockEngine.Vulkan;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ZLinq;

namespace RockEngine.Editor.Rendering.Passes.SubPasses
{
    public class PickingSubPass : IRenderSubPass
    {
        private readonly VulkanContext _context;
        private readonly GraphicsContext _graphicsEngine;
        private readonly BindingManager _bindingManager;
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly GlobalUbo _globalUbo;
        private readonly GlobalGeometryBuffer _globalGeometryBuffer;
        private readonly PipelineManager _pipelineManager;
        private readonly Bool32 _supportsMultiDraw;
        private readonly int _indirectCommandStride;
        private RckPipeline _pipeline;

        public PickingSubPass(
            VulkanContext context,
            GraphicsContext graphicsEngine,
            BindingManager bindingManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo, GlobalGeometryBuffer globalGeometryBuffer, PipelineManager pipelineManager)
        {
            _context = context;
            _graphicsEngine = graphicsEngine;
            _bindingManager = bindingManager;
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _globalUbo = globalUbo;
            _globalGeometryBuffer = globalGeometryBuffer;
            _pipelineManager = pipelineManager;
            _supportsMultiDraw = GetMultiDrawIndirectFeature();
            _indirectCommandStride = Marshal.SizeOf<DrawIndexedIndirectCommand>();
        }

        public static uint Order => 0;

        public static string Name => "picking";

        public void Execute(VkCommandBuffer cmd, params object[] args)
        {

            using (PerformanceTracer.BeginSection(nameof(PickingSubPass)))
            {
                uint frameIndex = (uint)args[0];
                var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(args), nameof(Camera));
                var camIndex = (int)args[2];
                var renderTarget = (PickingRenderTarget)args[3];

                cmd.SetViewport(renderTarget.Viewport);
                cmd.SetScissor(renderTarget.Scissor);

                var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
                var globalUboBinding = _globalUbo.GetBinding((uint)camIndex);
                var drawGroups = _indirectCommands.GetDrawGroups(GeometryPass.Name);
                var lightDrawGroups = _indirectCommands.GetDrawGroups(PostLightPass.Name);
                drawGroups.AddRange(lightDrawGroups);
                var pickingGroups = _indirectCommands.GetDrawGroups(PickingSubPass.Name);
                var drawGroupsSpan = CollectionsMarshal.AsSpan(drawGroups
                .AsValueEnumerable()
                .Where(s => !pickingGroups.Any(x => x.MeshRenderer == s.MeshRenderer))
                .ToList());
                var indirectBuffer = _indirectCommands.IndirectBuffer.Buffer;
                if (drawGroups.Count == 0 && pickingGroups.Count == 0)
                {
                    return;
                }

                _globalGeometryBuffer.Bind(cmd);

                Span<uint> dynamicOffsets = stackalloc uint[2];
                dynamicOffsets[0] = 0;
                dynamicOffsets[1] = 0;

                cmd.BindPipeline(_pipeline);
                _bindingManager.BindResource(frameIndex, globalUboBinding, cmd, _pipeline.Layout);
                _bindingManager.BindResource(frameIndex, matrixBinding, cmd, _pipeline.Layout);
                for (int i = 0; i < drawGroupsSpan.Length; i++)
                {
                    ref readonly var drawGroup = ref drawGroupsSpan[i];

                    if (!camera.CanRender(drawGroup.MeshRenderer.Entity) || camera.Entity == drawGroup.MeshRenderer.Entity)
                    {
                        continue;
                    }

                    // Get entity ID for picking
                    uint entityId = drawGroup.MeshRenderer.Entity.ID;

                    // Push entity ID as push constant
                    cmd.PushConstants(_pipeline.Layout, ShaderStageFlags.FragmentBit,
                        0, sizeof(uint), ref entityId);
                    //drawGroup.MaterialPass.CmdPushConstants(cmd);

                    // Issue draw command
                    if (_supportsMultiDraw)
                    {
                        VulkanContext.Vk.CmdDrawIndexedIndirect(cmd, indirectBuffer, drawGroup.ByteOffset,
                            drawGroup.Count, (uint)_indirectCommandStride);
                    }
                    else
                    {
                        for (uint j = 0; j < drawGroup.Count; j++)
                        {
                            VulkanContext.Vk.CmdDrawIndexedIndirect(cmd, indirectBuffer,
                                drawGroup.ByteOffset + (ulong)(j * _indirectCommandStride), 1,
                                (uint)_indirectCommandStride);
                        }
                    }
                }

                drawGroupsSpan = CollectionsMarshal.AsSpan(pickingGroups);
                RckPipeline? lastPipeline = null;
                MaterialPass? lastMaterialPass = null;
                dynamicOffsets = stackalloc uint[2];
                dynamicOffsets[0] = 0;
                dynamicOffsets[1] = 0;

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
                        cmd.BindPipeline(drawGroup.MaterialPass.Pipeline);
                        lastPipeline = drawGroup.MaterialPass.Pipeline;

                        // Bind global resources using the binding manager
                        _bindingManager.BindResource(frameIndex, globalUboBinding, cmd, drawGroup.MaterialPass.Pipeline.Layout);
                        _bindingManager.BindResource(frameIndex, matrixBinding, cmd, drawGroup.MaterialPass.Pipeline.Layout);
                    }

                    // Material change
                    if (lastMaterialPass != drawGroup.MaterialPass)
                    {
                        lastMaterialPass = drawGroup.MaterialPass;
                        _bindingManager.BindResourcesForMaterial(
                          frameIndex,
                          lastMaterialPass,
                          cmd,
                          false,
                          [matrixBinding.SetLocation, globalUboBinding.SetLocation]
                      );
                        lastMaterialPass.CmdPushConstants(cmd);
                    }

                    // Issue draw command
                    if (drawGroup.IsMultiDraw && _supportsMultiDraw)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Bool32 GetMultiDrawIndirectFeature()
        {
            return _context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
        }
        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
            // Color Attachment for picking (R8G8B8A8Unorm)
            builder.ConfigureAttachment(Format.R8G8B8A8Unorm)
                .WithColorOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.Store,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.TransferSrcOptimal) // We want to transfer from this image to read it
                .Add();

            // Depth Attachment
            builder.ConfigureAttachment(_graphicsEngine.Swapchain.DepthFormat)
                .WithDepthOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.DontCare,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.DepthStencilAttachmentOptimal)
                .Add();
        }

        public void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass)
        {
            // Color attachment (picking)
            subpass.AddColorAttachment(0, ImageLayout.ColorAttachmentOptimal);

            // Depth attachment
            subpass.SetDepthAttachment(1, ImageLayout.DepthStencilAttachmentOptimal);
        }

        public void SetupDependencies(RenderPassBuilder builder, uint subpassIndex)
        {
            // External -> PickingPass dependency
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
            var shaderManager = IoC.Container.GetInstance<IShaderManager>();
            var vertShader = VkShaderModule.Create(_context, shaderManager.GetShader("Picking.vert"), ShaderStageFlags.VertexBit);
            var fragShader = VkShaderModule.Create(_context, shaderManager.GetShader("Picking.frag"), ShaderStageFlags.FragmentBit);
            var colorBlendAttachments = new PipelineColorBlendAttachmentState[1]
              {
                    new PipelineColorBlendAttachmentState
                    {
                        ColorWriteMask = ColorComponentFlags.RBit |
                                        ColorComponentFlags.GBit |
                                        ColorComponentFlags.BBit |
                                        ColorComponentFlags.ABit,
                        BlendEnable = false
                    }
              };
            using var pipelineBuilder = GraphicsPipelineBuilder.CreateDefault<PickingPassStrategy>(_context, "Picking", IoC.Container, [vertShader, fragShader]);
            pipelineBuilder.WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(colorBlendAttachments))
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                     .Add(PositionVertex.GetBindingDescription(), PositionVertex.GetAttributeDescriptions()))
                .WithSubpass<PickingSubPass>()
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.Less,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false,
                });

            _pipeline = _pipelineManager.Create(pipelineBuilder);

        }


        public SubPassMetadata GetMetadata()
        {
            return new(Order, Name);
        }
    }
}