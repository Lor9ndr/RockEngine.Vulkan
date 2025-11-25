using RockEngine.Core.Builders;
using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Passes.SubPasses
{
    public class GeometryPass : IRenderSubPass
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
        private object _pipeline;

        public GeometryPass(
            VulkanContext context,
            GraphicsContext graphicsEngine,
            BindingManager bindingManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo,
            GlobalGeometryBuffer globalGeometryBuffer,
            PipelineManager pipelineManager)
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

        public static string Name => "geometry";

        public void Execute(VkCommandBuffer cmd, params object[] args)
        {
            using (PerformanceTracer.BeginSection(nameof(GeometryPass)))
            {
                uint frameIndex = (uint)args[0];
                var camera = args[1] as Camera ?? throw new ArgumentNullException(nameof(args), nameof(Camera));
                var camIndex = (int)args[2];

                cmd.SetViewport(camera.RenderTarget.Viewport);
                cmd.SetScissor(camera.RenderTarget.Scissor);

                var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
                var globalUboBinding = _globalUbo.GetBinding((uint)camIndex);

                var drawGroups = _indirectCommands.GetDrawGroups(Name);
                if (drawGroups.Count == 0)
                {
                    return;
                }

                // Get the span of draw groups
                var drawGroupsSpan = CollectionsMarshal.AsSpan(drawGroups);

                // Pre-calculate common values
                var indirectBuffer = _indirectCommands.IndirectBuffer.Buffer;

                // Track state
                RenderState currentState = default;

                _globalGeometryBuffer.Bind(cmd);

                unsafe
                {
                    uint* dynamicOffsets = stackalloc uint[2];
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
                        if (!ReferenceEquals(currentState.Pipeline, drawGroup.MaterialPass.Pipeline))
                        {
                            cmd.BindPipeline(drawGroup.MaterialPass.Pipeline);
                            currentState.Pipeline = drawGroup.MaterialPass.Pipeline;

                            _bindingManager.BindResource(frameIndex, globalUboBinding, cmd, currentState.Pipeline.Layout);
                            _bindingManager.BindResource(frameIndex, matrixBinding, cmd, currentState.Pipeline.Layout);
                        }

                        // Material change
                        if (!ReferenceEquals(currentState.MaterialPass, drawGroup.MaterialPass))
                        {
                            currentState.MaterialPass = drawGroup.MaterialPass;
                            _bindingManager.BindResourcesForMaterial(
                                frameIndex,
                                currentState.MaterialPass,
                                cmd,
                                false,
                                [matrixBinding.SetLocation, globalUboBinding.SetLocation]
                            );
                            currentState.MaterialPass.CmdPushConstants(cmd);
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
        }
        private struct RenderState
        {
            public RckPipeline Pipeline;
            public MaterialPass MaterialPass;

            public RenderState(RckPipeline pipeline, MaterialPass materialPass)
            {
                Pipeline = pipeline;
                MaterialPass = materialPass;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Bool32 GetMultiDrawIndirectFeature()
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
            var shaderManager = IoC.Container.GetInstance<IShaderManager>();
            VkShaderModule vkShaderModuleFrag = VkShaderModule.Create(_context, shaderManager.GetShader("Geometry.frag"), ShaderStageFlags.FragmentBit);

            VkShaderModule vkShaderModuleVert = VkShaderModule.Create(_context, shaderManager.GetShader("Geometry.vert"), ShaderStageFlags.VertexBit);

            var pipelineLayout = VkPipelineLayout.Create(_context, vkShaderModuleVert, vkShaderModuleFrag);

            var binding_desc = new VertexInputBindingDescription();
            binding_desc.Stride = (uint)Unsafe.SizeOf<Vertex>();
            binding_desc.InputRate = VertexInputRate.Vertex;

            var colorBlendAttachments = new PipelineColorBlendAttachmentState[GBuffer.ColorAttachmentFormats.Length];
            for (int i = 0; i < GBuffer.ColorAttachmentFormats.Length; i++)
            {
                colorBlendAttachments[i] = new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit |
                                    ColorComponentFlags.GBit |
                                    ColorComponentFlags.BBit |
                                    ColorComponentFlags.ABit,
                    BlendEnable = false
                };
            }


            using GraphicsPipelineBuilder pipelineBuilder = new GraphicsPipelineBuilder(_context, "Geometry")
                 .WithShaderModule(vkShaderModuleVert)
                 .WithShaderModule(vkShaderModuleFrag)
                 .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.FrontBit).FrontFace(FrontFace.Clockwise))
                 .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                 .WithVertexInputState<Vertex>()
                 .WithViewportState(new VulkanViewportStateInfoBuilder()
                     .AddViewport(new Viewport() { Height = _graphicsEngine.Swapchain.Surface.Size.Y, Width = _graphicsEngine.Swapchain.Surface.Size.X })
                     .AddScissors(new Rect2D()
                     {
                         Offset = new Offset2D(),
                         Extent = new Extent2D((uint?)_graphicsEngine.Swapchain.Surface.Size.X, (uint?)_graphicsEngine.Swapchain.Surface.Size.Y)
                     }))
                 .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                 .WithColorBlendState(new VulkanColorBlendStateBuilder()
                     .AddAttachment(colorBlendAttachments))
                 .AddRenderPass<DeferredPassStrategy>(IoC.Container.GetInstance<RenderPassManager>())
                 .WithPipelineLayout(pipelineLayout)
                 .WithSubpass<GeometryPass>()
                 .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor)
                    )
                 .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo()
                 {
                     SType = StructureType.PipelineDepthStencilStateCreateInfo,
                     DepthTestEnable = true,
                     DepthWriteEnable = true,
                     DepthCompareOp = CompareOp.Less,
                     DepthBoundsTestEnable = false,
                     MinDepthBounds = 0.0f,
                     MaxDepthBounds = 1.0f,
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