using RockEngine.Core.Builders;
using RockEngine.Core.DI;
using RockEngine.Core.Diagnostics;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
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

        public void Execute(UploadBatch batch, params object[] args)
        {
            using var tracer = PerformanceTracer.BeginSection(nameof(ShadowPass));

            uint frameIndex = (uint)args[0];
            var light = args[1] as Light ?? throw new ArgumentNullException(nameof(Light));

            var matrixBinding = _transformManager.GetCurrentBinding(frameIndex);
            using var material = new Material("ShadowMaterial");

            RckPipeline pipeline;
            ref var lightData = ref light.GetLightData();

            switch (light.Type)
            {
                case LightType.Directional when light.CascadeCount > 1:
                    {
                        pipeline = _csmShadowPipeline;
                        var pass = new MaterialPass(_csmShadowPipeline);
                        material.AddPass(Name, pass);
                        pass.BindResource(_shadowManager.GetCSMDataBinding());
                        break;
                    }

                case LightType.Point:
                    {
                        pipeline = _pointShadowPipeline;
                        var pass = new MaterialPass(_pointShadowPipeline);
                        material.AddPass(Name, pass);
                        pass.BindResource(_shadowManager.GetShadowMatricesBinding());

                        ShadowPointPushConstants pushConstants = new ShadowPointPushConstants
                        {
                            LightPosition = new Vector4(light.Entity.Transform.WorldPosition,0),
                            FarPlane = light.Radius,
                            ShadowIndex = (uint)lightData.ShadowParams.W
                        };
                        pass.PushConstant("pc", in pushConstants);
                        break;
                    }

                default:
                    {
                        pipeline = _directionalShadowPipeline;
                        var pass = new MaterialPass(_directionalShadowPipeline);
                        material.AddPass(Name, pass);

                        var shadowMatrix = light.GetShadowMatrix()[0];

                        ShadowSpotPushConstants pushConstants = new ShadowSpotPushConstants
                        {
                            ShadowMatrix = shadowMatrix,
                        };
                        pass.PushConstant("pc", in pushConstants);
                        break;
                    }
            }

            // Pre-calculate viewport and scissor
            var viewport = new Viewport(0, 0, light.ShadowMapSize, light.ShadowMapSize, 0, 1);
            var scissor = new Rect2D(new Offset2D(), new Extent2D(light.ShadowMapSize, light.ShadowMapSize));

            batch.SetViewport(viewport);
            batch.SetScissor(scissor);

            batch.BindPipeline(pipeline);
            var materialPass = material.GetPass(Name);

            _globalGeometryBuffer.Bind(batch);

            // Push constants based on light type
            materialPass.CmdPushConstants(batch);
            materialPass.BindResource(matrixBinding);
            _bindingManager.BindResourcesForMaterial(frameIndex, materialPass, batch);

            // Render shadow-casting geometry
            var drawGroups = CollectionsMarshal.AsSpan(_indirectCommands.GetDrawGroups<GeometryPass>());
            var indirectBuffer = _indirectCommands.IndirectBuffer.Buffer;

            foreach (ref readonly var drawGroup in drawGroups)
            {
                if (drawGroup.IsMultiDraw && _supportsMultiDraw)
                {
                    batch.DrawIndexedIndirect(
                        indirectBuffer,
                        drawGroup.Count,
                        drawGroup.ByteOffset,
                        (uint)_indirectCommandStride);
                }
                else
                {
                    for (uint j = 0; j < drawGroup.Count; j++)
                    {
                            batch.DrawIndexedIndirect(
                            indirectBuffer,
                            1,
                            drawGroup.ByteOffset + (ulong)(j * _indirectCommandStride),
                            (uint)_indirectCommandStride);
                    }
                }
            }
        }

        [GLSLStruct]
        public struct ShadowPointPushConstants
        {
            public System.Numerics.Vector4 LightPosition;      // Used for point lights
            public float FarPlane;             // Used for point lights
            public uint ShadowIndex;           // Index into shadow map array
        }

        [GLSLStruct(GLSLMemoryLayout.Std140)]

        public struct ShadowSpotPushConstants
        {
            public Matrix4x4 ShadowMatrix;     // Used for directional/spot lights (non-CSM)
        }

        [GLSLStruct(GLSLMemoryLayout.Std140)]
        public struct ShadowCSMPushConstants
        {
            public Vector4 LightDirection;     // Used for CSM directional lights
            public uint CascadeCount;          // Used for CSM
            public uint ShadowIndex;           // Index into shadow map array
            private float _padding1;
            private float _padding2;
        }

        public void SetupAttachmentDescriptions(RenderPassBuilder builder)
        {
            builder.ConfigureAttachment(Format.D32Sfloat)
                .WithDepthOperations(
                    load: AttachmentLoadOp.Clear,
                    store: AttachmentStoreOp.Store,
                    initialLayout: ImageLayout.DepthStencilAttachmentOptimal,
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
                        PipelineStageFlags.VertexShaderBit,
                        PipelineStageFlags.LateFragmentTestsBit)
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