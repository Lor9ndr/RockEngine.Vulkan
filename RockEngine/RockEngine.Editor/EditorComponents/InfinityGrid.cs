using RockEngine.Core;
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Builders;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.ResourceProviders;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Editor.EditorComponents
{
    public partial class InfinityGrid : Component
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct GridMaterial
        {
            public Vector4 GridColor;
            public Vector4 MajorGridColor;
            public Vector4 AxisColor;
            public Vector4 AxisColorZ;
            public float GridStep;
            public float MajorGridStep;
        }

        public struct GridPushConstants
        {
            public Vector3 cameraPosition;
            public float gridScale;
            public Matrix4x4 viewProj;
            public Matrix4x4 model;
        }

        private const float GRID_STEP = 1.0f;
        private const float MAJOR_GRID_STEP = 10.0f;

        private Material _material;
        private bool _isInitialized = false;

        public Vector4 GridColor { get; set; } = new Vector4(0.5f, 0.5f, 0.5f, 0.3f);
        public Vector4 MajorGridColor { get; set; } = new Vector4(0.8f, 0.8f, 0.8f, 0.5f);
        public Vector4 AxisColor { get; set; } = new Vector4(1.0f, 0.0f, 0.0f, 1.0f); // X - Red
        public Vector4 AxisColorZ { get; set; } = new Vector4(0.0f, 0.0f, 1.0f, 1.0f); // Z - Blue
        public float GridScale { get; set; } = 1.0f;

        private UniformBuffer _uniformBuffer;

        public override async ValueTask OnStart(Renderer renderer)
        {
            await InitializeGrid(renderer);
            Entity.Layer = IoC.Container.GetInstance<RenderLayerSystem>().Debug;
            _isInitialized = true;
        }

        public override async ValueTask Update(Renderer renderer)
        {
            if (!_isInitialized)
            {
                return;
            }

            // Get camera position and view projection matrix
            var cameraPos = Entity.Transform.Position;
            var camera = Entity.GetComponent<DebugCamera>();
            var viewProj = camera.ViewProjectionMatrix;

            // Create a large quad that follows the camera (but grid is generated in shader)
            var modelMatrix = Matrix4x4.CreateScale(1000.0f) *
                            Matrix4x4.CreateTranslation(cameraPos.X, 0, cameraPos.Z);

            _material.PushConstant("push", new GridPushConstants()
            {
                cameraPosition = cameraPos,
                gridScale = GridScale,
                viewProj = viewProj,
                model = modelMatrix
            });

            await _uniformBuffer.UpdateAsync(new GridMaterial()
            {
                AxisColor = AxisColor,
                AxisColorZ = AxisColorZ,
                GridColor = GridColor,
                GridStep = GRID_STEP,
                MajorGridColor = MajorGridColor,
                MajorGridStep = MAJOR_GRID_STEP
            });
        }

        private async ValueTask InitializeGrid(Renderer renderer)
        {
            // Simple quad geometry - the grid will be generated in the shader
            var (vertices, indices) = GenerateQuadGeometry();

            _material = new Material("InfinityGrid");

            // Create grid material
            var vertShader = await VkShaderModule.CreateAsync(renderer.Context, "Shaders/Grid.vert.spv", ShaderStageFlags.VertexBit);
            var fragShader = await VkShaderModule.CreateAsync(renderer.Context, "Shaders/Grid.frag.spv", ShaderStageFlags.FragmentBit);

            var pipeline = CreateGridPipeline(renderer, vertShader, fragShader);
            _material.AddPass(PostLightPass.Name, new MaterialPass(pipeline));

            _uniformBuffer = new UniformBuffer(renderer.Context, 0, (ulong)Marshal.SizeOf<GridMaterial>(), false);

            await _uniformBuffer.UpdateAsync(new GridMaterial()
            {
                AxisColor = AxisColor,
                AxisColorZ = AxisColorZ,
                GridColor = GridColor,
                GridStep = GRID_STEP,
                MajorGridColor = MajorGridColor,
                MajorGridStep = MAJOR_GRID_STEP
            });

            _material.BindResource(new UniformBufferBinding(_uniformBuffer, 0, 4));

            Entity.AddComponent<MeshRenderer>()
                .SetProviders(
                new MeshProvider<PositionVertex>(new MeshData<PositionVertex>(vertices, indices)),
                new MaterialProvider(_material));
        }

        private RckPipeline CreateGridPipeline(Renderer renderer, VkShaderModule vertShader, VkShaderModule fragShader)
        {
            using var pipelineBuilder = GraphicsPipelineBuilder.CreateDefault(VulkanContext.GetCurrent(), "InfinityGrid", renderer.RenderPass, [vertShader, fragShader]);

            pipelineBuilder
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(PositionVertex.GetBindingDescription(), PositionVertex.GetAttributeDescriptions()))
                .WithSubpass<PostLightPass>()
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.LessOrEqual,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false,
                })
                .WithRasterizer(new VulkanRasterizerBuilder()
                    .PolygonMode(PolygonMode.Fill)
                    .CullFace(CullModeFlags.None)
                    .FrontFace(FrontFace.Clockwise)
                    .DepthBiasEnabe(false))
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure(topology: PrimitiveTopology.TriangleList))
                .WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(new PipelineColorBlendAttachmentState
                    {
                        BlendEnable = true,
                        SrcColorBlendFactor = BlendFactor.SrcAlpha,
                        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        ColorBlendOp = BlendOp.Add,
                        SrcAlphaBlendFactor = BlendFactor.One,
                        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        AlphaBlendOp = BlendOp.Add,
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
                    }));

            return renderer.PipelineManager.Create(pipelineBuilder);
        }

        private (PositionVertex[] vertices, uint[] indices) GenerateQuadGeometry()
        {
            // Simple quad covering a large area - grid is generated in shader
            var vertices = new[]
            {
                new PositionVertex(new Vector3(-1, 0, -1)),
                new PositionVertex(new Vector3( 1, 0, -1)),
                new PositionVertex(new Vector3( 1, 0,  1)),
                new PositionVertex(new Vector3(-1, 0,  1)),
            };

            var indices = new uint[] { 0, 1, 2, 2, 3, 0 };

            return (vertices, indices);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PositionVertex : IVertex
        {
            public Vector3 Position;

            public PositionVertex(Vector3 position)
            {
                Position = position;
            }

            public static VertexInputBindingDescription GetBindingDescription() => new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)Marshal.SizeOf<PositionVertex>(),
                InputRate = VertexInputRate.Vertex
            };

            public static VertexInputAttributeDescription[] GetAttributeDescriptions()
            {
                return new[]
                {
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 0,
                        Format = Format.R32G32B32Sfloat,
                        Offset = 0
                    }
                };
            }
        }
    }
}