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

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Passes.SubPasses
{
    public sealed class ShadowPass : IRenderSubPass
    {
        private readonly VulkanContext _context;
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly GlobalGeometryBuffer _globalGeometryBuffer;
        private readonly PipelineManager _pipelineManager;
        private readonly BindingManager _bindingManager;
        private readonly ShadowManager _shadowManager;
        private readonly LightManager _lightManager;

        private RckPipeline _directionalShadowPipeline;
        private RckPipeline _pointShadowPipeline;
        private RckPipeline _csmShadowPipeline; 
        private Bool32 _supportsMultiDraw;
        private int _indirectCommandStride;
        private bool _disposed;

        public static uint Order => 0;
        public static string Name => "shadow";

        public ShadowPass(
            VulkanContext context,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalGeometryBuffer globalGeometryBuffer,
            PipelineManager pipelineManager,
            BindingManager bindingManager,
            ShadowManager shadowManager,
            LightManager lightManager)
        {
            _context = context;
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _globalGeometryBuffer = globalGeometryBuffer;
            _pipelineManager = pipelineManager;
            _bindingManager = bindingManager;
            _shadowManager = shadowManager;
            _lightManager = lightManager;
        }

        public SubPassMetadata GetMetadata() => new(Order, Name);

        public void Initilize()
        {
            CreateShadowPipelines();
            _supportsMultiDraw = _context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
            _indirectCommandStride = Unsafe.SizeOf<DrawIndexedIndirectCommand>();
        }

        private void CreateShadowPipelines()
        {
            // Directional/Spot light shadow pipeline (single layer)
            using var dirVertShader = VkShaderModule.Create(_context, "Shaders/Shadow.vert.spv", ShaderStageFlags.VertexBit);
            using var dirFragShader = VkShaderModule.Create(_context, "Shaders/Shadow.frag.spv", ShaderStageFlags.FragmentBit);

            using var dirPipelineBuilder = GraphicsPipelineBuilder.CreateDefault<ShadowPassStrategy>(_context, "ShadowDirectional", IoC.Container, [dirVertShader, dirFragShader]);
            ConfigureDirectionalPipeline(dirPipelineBuilder);
            _directionalShadowPipeline = _pipelineManager.Create(dirPipelineBuilder);

            // Point light shadow pipeline (with geometry shader)
            var shaderManager = IoC.Container.GetInstance<ShaderManager>();
            using var pointVertShader = VkShaderModule.Create(_context, shaderManager.GetShader("PointShadow.vert"), ShaderStageFlags.VertexBit);
            using var pointGeomShader = VkShaderModule.Create(_context, shaderManager.GetShader("PointShadow.geom"), ShaderStageFlags.GeometryBit);
            using var pointFragShader = VkShaderModule.Create(_context, shaderManager.GetShader("PointShadow.frag"), ShaderStageFlags.FragmentBit);

            using var pointPipelineBuilder = GraphicsPipelineBuilder.CreateDefault<ShadowPassStrategy>(_context, "ShadowPoint", IoC.Container, [pointVertShader, pointGeomShader, pointFragShader]);
            ConfigurePointPipeline(pointPipelineBuilder);
            _pointShadowPipeline = _pipelineManager.Create(pointPipelineBuilder);

            // NEW: CSM pipeline for directional lights
             var csmVertShader = VkShaderModule.Create(_context, shaderManager.GetShader("CSMShadow.vert"), ShaderStageFlags.VertexBit);
             var csmGeomShader = VkShaderModule.Create(_context, shaderManager.GetShader("CSMShadow.geom"), ShaderStageFlags.GeometryBit);
             var csmFragShader = VkShaderModule.Create(_context, shaderManager.GetShader("CSMShadow.frag"), ShaderStageFlags.FragmentBit);

            using var csmPipelineBuilder = GraphicsPipelineBuilder.CreateDefault<ShadowPassStrategy>(_context, "ShadowCSM", IoC.Container, [csmVertShader, csmGeomShader, csmFragShader]);
            ConfigureCSMPipeline(csmPipelineBuilder);
            _csmShadowPipeline = _pipelineManager.Create(csmPipelineBuilder);
        }

        private void ConfigureDirectionalPipeline(GraphicsPipelineBuilder builder)
        {
            builder
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                .WithRasterizer(new VulkanRasterizerBuilder()
                    .CullFace(CullModeFlags.BackBit)
                    .DepthBiasEnabe(true)
                    .DepthBiasConstantFactor(1.25f)
                    .DepthBiasClamp(0.0f)
                    .DepthBiasSlopeFactor(1.75f))
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.Less,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false
                })
                .WithColorBlendState(new VulkanColorBlendStateBuilder())
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor))
                .WithSubpass<ShadowPass>();
        }

        private void ConfigurePointPipeline(GraphicsPipelineBuilder builder)
        {
            builder
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                .WithRasterizer(new VulkanRasterizerBuilder()
                    .CullFace(CullModeFlags.BackBit)
                    .DepthBiasEnabe(true)
                    .DepthBiasConstantFactor(1.25f)
                    .DepthBiasClamp(0.0f)
                    .DepthBiasSlopeFactor(1.75f))
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.Less,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false
                })
                .WithColorBlendState(new VulkanColorBlendStateBuilder())
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor))
                .WithSubpass<ShadowPass>();
        }

        private void ConfigureCSMPipeline(GraphicsPipelineBuilder builder)
        {
            builder
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                .WithRasterizer(new VulkanRasterizerBuilder()
                    .CullFace(CullModeFlags.BackBit)
                    .DepthBiasEnabe(true)
                    .DepthBiasConstantFactor(1.25f)
                    .DepthBiasClamp(0.0f)
                    .DepthBiasSlopeFactor(1.75f))
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.Less,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false
                })
                .WithColorBlendState(new VulkanColorBlendStateBuilder())
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor))
                .WithSubpass<ShadowPass>();
        }

        public void Execute(VkCommandBuffer cmd, params object[] args)
        {
            using var tracer = PerformanceTracer.BeginSection(nameof(ShadowPass));

            uint frameIndex = (uint)args[0];
            var light = args[1] as Light ?? throw new ArgumentNullException(nameof(Light));

            var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
            using var material = new Material("ShadowMaterial");

            RckPipeline pipeline;
            ref var lightData = ref light.GetLightData();

            if (light.Type == LightType.Directional && light.CascadeCount > 1)
            {
                pipeline = _csmShadowPipeline;
                var pass = new MaterialPass(_csmShadowPipeline);
                material.AddPass(Name, pass);
                pass.BindResource(_shadowManager.GetCSMDataBinding());
            }
            else if (light.Type == LightType.Point)
            {
                pipeline = _pointShadowPipeline;
                var pass = new MaterialPass(_pointShadowPipeline);
                material.AddPass(Name, pass);
                pass.BindResource(_shadowManager.GetShadowMatricesBinding());

                ShadowPointPushConstants pushConstants = new ShadowPointPushConstants
                {
                    LightPosition = light.Entity.Transform.Position,
                    FarPlane = light.ShadowFarPlane,
                    ShadowIndex = (uint)lightData.ShadowParams.W
                };
                pass.PushConstant("pc", in pushConstants);
            }
            else
            {
                // CORRECTED: Use directional pipeline for both directional and spot lights
                pipeline = _directionalShadowPipeline;
                var pass = new MaterialPass(_directionalShadowPipeline);
                material.AddPass(Name, pass);

                // CORRECTED: For spot lights, use the first (and only) matrix
                var shadowMatrix = light.GetShadowMatrix()[0];

                ShadowSpotPushConstants pushConstants = new ShadowSpotPushConstants
                {
                    ShadowMatrix = shadowMatrix,
                };
                pass.PushConstant("pc", in pushConstants);
            }

            // Pre-calculate viewport and scissor
            var viewport = new Viewport(0, 0, light.ShadowMapSize, light.ShadowMapSize, 0, 1);
            var scissor = new Rect2D(new Offset2D(), new Extent2D(light.ShadowMapSize, light.ShadowMapSize));

            cmd.SetViewport(viewport);
            cmd.SetScissor(scissor);

            cmd.BindPipeline(pipeline);
            var materialPass = material.GetPass(Name);

            _globalGeometryBuffer.Bind(cmd);

            // Push constants based on light type
            materialPass.CmdPushConstants(cmd);
            materialPass.BindResource(matrixBinding);
            _bindingManager.BindResourcesForMaterial(frameIndex, materialPass, cmd);

            // Render shadow-casting geometry
            var drawGroups = CollectionsMarshal.AsSpan(_indirectCommands.GetDrawGroups(GeometryPass.Name));
            var indirectBuffer = _indirectCommands.IndirectBuffer.Buffer;

            foreach (ref readonly var drawGroup in drawGroups)
            {
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


        [StructLayout(LayoutKind.Sequential)]
        public struct ShadowPointPushConstants
        {
            public Vector3 LightPosition;      // Used for point lights
            public float FarPlane;             // Used for point lights
            public uint ShadowIndex;           // Index into shadow map array
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct ShadowSpotPushConstants
        {
            public Matrix4x4 ShadowMatrix;     // Used for directional/spot lights (non-CSM)
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ShadowCSMPushConstants
        {
            public Vector3 LightDirection;     // Used for CSM directional lights
            public uint CascadeCount;          // Used for CSM
            public uint ShadowIndex;           // Index into shadow map array
        }

        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
            builder.ConfigureAttachment(Format.D32Sfloat)
                .WithDepthOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.Store,
                    initialLayout: ImageLayout.Undefined,
                    finalLayout: ImageLayout.TransferSrcOptimal)
                .Add();
        }

        public void SetupSubpassDescription(RenderPassBuilder.SubpassConfigurer subpass)
        {
            subpass.SetDepthAttachment(0, ImageLayout.DepthStencilAttachmentOptimal);
        }

        public void SetupDependencies(RenderPassBuilder builder, uint subpassIndex)
        {
            if (subpassIndex == 0)
            {
                builder.AddDependency()
                    .FromExternal()
                    .ToSubpass(subpassIndex)
                    .WithStages(
                        PipelineStageFlags.TopOfPipeBit,
                        PipelineStageFlags.EarlyFragmentTestsBit)
                    .WithAccess(
                        AccessFlags.None,
                        AccessFlags.DepthStencilAttachmentWriteBit)
                    .Add();

                builder.AddDependency()
                    .FromSubpass(subpassIndex)
                    .ToExternal()
                    .WithStages(
                        PipelineStageFlags.LateFragmentTestsBit,
                        PipelineStageFlags.TransferBit)
                    .WithAccess(
                        AccessFlags.DepthStencilAttachmentWriteBit,
                        AccessFlags.TransferReadBit)
                    .Add();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _directionalShadowPipeline?.Dispose();
            _pointShadowPipeline?.Dispose();
            _csmShadowPipeline?.Dispose(); 
            _disposed = true;
        }
    }
}