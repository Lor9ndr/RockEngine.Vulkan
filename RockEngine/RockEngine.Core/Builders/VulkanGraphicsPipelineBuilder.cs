using Microsoft.Extensions.DependencyInjection;

using RockEngine.Core.DI;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

using SimpleInjector;

using System.Buffers;
using System.Text;

namespace RockEngine.Core.Builders
{
    public class GraphicsPipelineBuilder : DisposableBuilder
    {
        private MemoryHandle _entryPoint;
        private readonly VulkanContext _context;
        private readonly string _name;
        private RckRenderPass _renderPass;
        private VkPipelineLayout _pipelineLayout;
        private readonly PipelineStageBuilder _pipelineStageBuilder = new PipelineStageBuilder();
        private VulkanPipelineVertexInputStateBuilder _vertexInputStateBuilder;
        private VulkanInputAssemblyBuilder _inputAssemblyBuilder;
        private VulkanViewportStateInfoBuilder? _viewportStateBuilder;
        private VulkanRasterizerBuilder _rasterizerBuilder;
        private VulkanMultisampleStateInfoBuilder _multisampleStateBuilder;
        private VulkanColorBlendStateBuilder _colorBlendStateBuilder;
        private PipelineDynamicStateBuilder _dynamicStateBuilder;
        private PipelineDepthStencilStateCreateInfo _depthStencilState;
        private SubPassMetadata _subpassMetadata;

        public GraphicsPipelineBuilder(VulkanContext context, string name)
        {
            _entryPoint = CreateMemoryHandle(Encoding.ASCII.GetBytes("main"));
            _context = context;
            _name = name;
        }

        public static GraphicsPipelineBuilder CreateDefault(VulkanContext context, string name, RckRenderPass renderPass, params VkShaderModule[] shaders)
        {
            var builder =  new GraphicsPipelineBuilder(context, name)
                .WithShaderModule(shaders)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder())
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, 1280, 720, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), new Extent2D(1280, 720))))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithPipelineLayout(VkPipelineLayout.Create(context, shaders))
                .AddRenderPass(renderPass)
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo()
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = false,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.Always,
                    DepthBoundsTestEnable = false,
                    MinDepthBounds = 0.0f,
                    MaxDepthBounds = 1.0f,
                    StencilTestEnable = false,
                })
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor));

            return builder;
        }
        public static GraphicsPipelineBuilder CreateDefault<TRenderPassStrategy>(VulkanContext context, string name, IServiceProvider container, params VkShaderModule[] shaders) where TRenderPassStrategy : class,IRenderPassStrategy
        {
            var renderPassStrategy = container.GetService<TRenderPassStrategy>();
            var builder = new GraphicsPipelineBuilder(context, name)
                .WithShaderModule(shaders)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder())
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, 1280, 720, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), new Extent2D(1280, 720))))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithPipelineLayout(VkPipelineLayout.Create(context, shaders))
                .AddRenderPass(renderPassStrategy.RenderPass ?? renderPassStrategy.BuildRenderPass())
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo()
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = false,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.Always,
                    DepthBoundsTestEnable = false,
                    MinDepthBounds = 0.0f,
                    MaxDepthBounds = 1.0f,
                    StencilTestEnable = false,
                })
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor));

            return builder;
        }
        private static bool IsColorFormat(Format format)
        {
            return format switch
            {
                Format.D16Unorm => false,
                Format.D32Sfloat => false,
                Format.S8Uint => false,
                Format.D16UnormS8Uint => false,
                Format.D24UnormS8Uint => false,
                Format.D32SfloatS8Uint => false,
                _ => true // Assume it's a color format if not explicitly a depth/stencil format
            };
        }

        public GraphicsPipelineBuilder AddRenderPass(RckRenderPass renderPass)
        {
            _renderPass = renderPass;
            return this;
        }

        public GraphicsPipelineBuilder WithSubpass(uint subpassIndex, string subpassName)
        {
            _subpassMetadata = new SubPassMetadata(subpassIndex, subpassName);
            return this;
        }

        public GraphicsPipelineBuilder WithSubpass<T>() where T : IRenderSubPass
        {
            _subpassMetadata = new SubPassMetadata(T.Order, T.Name);
            return this;
        }

        public GraphicsPipelineBuilder WithSubpass(SubPassMetadata metadata)
        {
            _subpassMetadata = metadata;
            return this;
        }

        public unsafe GraphicsPipelineBuilder WithShaderModule(VkShaderModule shaderModule)
        {
            _pipelineStageBuilder.AddStage(shaderModule.Stage, shaderModule, (byte*)_entryPoint.Pointer);
            return this;
        }

        public unsafe GraphicsPipelineBuilder WithShaderModule(params VkShaderModule[] shaderModules)
        {
            foreach (var item in shaderModules)
            {
                _pipelineStageBuilder.AddStage(item.Stage, item, (byte*)_entryPoint.Pointer);
            }
            return this;
        }

        public GraphicsPipelineBuilder WithVertexInputState(VulkanPipelineVertexInputStateBuilder vertexInputStateBuilder)
        {
            _vertexInputStateBuilder = vertexInputStateBuilder;
            return this;
        }
        public GraphicsPipelineBuilder WithVertexInputState<T>() where T: IVertex
        {
            _vertexInputStateBuilder = new VulkanPipelineVertexInputStateBuilder().Add<T>();
            return this;
        }

        public GraphicsPipelineBuilder WithInputAssembly(VulkanInputAssemblyBuilder inputAssemblyBuilder)
        {
            _inputAssemblyBuilder = inputAssemblyBuilder;
            return this;
        }

        public GraphicsPipelineBuilder WithViewportState(VulkanViewportStateInfoBuilder viewportStateBuilder)
        {
            _viewportStateBuilder = viewportStateBuilder;
            return this;
        }

        public GraphicsPipelineBuilder WithRasterizer(VulkanRasterizerBuilder rasterizerBuilder)
        {
            _rasterizerBuilder = rasterizerBuilder;
            return this;
        }

        public GraphicsPipelineBuilder WithMultisampleState(VulkanMultisampleStateInfoBuilder multisampleStateBuilder)
        {
            _multisampleStateBuilder = multisampleStateBuilder;
            return this;
        }

        public GraphicsPipelineBuilder WithColorBlendState(VulkanColorBlendStateBuilder colorBlendStateBuilder)
        {
            _colorBlendStateBuilder = colorBlendStateBuilder;
            return this;
        }

        public GraphicsPipelineBuilder WithDynamicState(PipelineDynamicStateBuilder dynamicStateBuilder)
        {
            _dynamicStateBuilder = dynamicStateBuilder;
            return this;
        }

        public GraphicsPipelineBuilder WithPipelineLayout(VkPipelineLayout pipelineLayout)
        {
            _pipelineLayout = pipelineLayout;
            return this;
        }

        public GraphicsPipelineBuilder AddDepthStencilState(PipelineDepthStencilStateCreateInfo pipelineDepthStencilStateCreateInfo)
        {
            _depthStencilState = pipelineDepthStencilStateCreateInfo;
            return this;
        }

        public GraphicsPipelineBuilder AddRenderPass<T>(RenderPassManager renderPassManager) where T : class, IRenderPassStrategy
        {
            _renderPass = renderPassManager.GetRenderPass<T>();
            return this;
        }

        public RckPipeline Build()
        {
            ValidateRequiredComponents();

            var vkPipeline = BuildVkPipeline();
            return new RckPipeline(vkPipeline, _name, _renderPass, _subpassMetadata, _pipelineLayout);
        }

        private void ValidateRequiredComponents()
        {
            ArgumentNullException.ThrowIfNull(_pipelineLayout, nameof(_pipelineLayout));
            ArgumentNullException.ThrowIfNull(_pipelineStageBuilder, nameof(_pipelineStageBuilder));
            ArgumentNullException.ThrowIfNull(_vertexInputStateBuilder, nameof(_vertexInputStateBuilder));
            ArgumentNullException.ThrowIfNull(_colorBlendStateBuilder, nameof(_colorBlendStateBuilder));
            ArgumentNullException.ThrowIfNull(_dynamicStateBuilder, nameof(_dynamicStateBuilder));
            ArgumentNullException.ThrowIfNull(_inputAssemblyBuilder, nameof(_inputAssemblyBuilder));
            ArgumentNullException.ThrowIfNull(_multisampleStateBuilder, nameof(_multisampleStateBuilder));
            ArgumentNullException.ThrowIfNull(_rasterizerBuilder, nameof(_rasterizerBuilder));
            ArgumentNullException.ThrowIfNull(_viewportStateBuilder, nameof(_viewportStateBuilder));
            ArgumentNullException.ThrowIfNull(_renderPass, nameof(_renderPass));

            if (_subpassMetadata.Equals(default))
            {
                throw new InvalidOperationException("Subpass metadata must be set before building the pipeline");
            }
        }

        private unsafe VkPipeline BuildVkPipeline()
        {
            using var pstages = _pipelineStageBuilder.Build();
            using var pInputState = _vertexInputStateBuilder.Build();
            using var pColorBlend = _colorBlendStateBuilder.Build();
            using var pDynamicState = _dynamicStateBuilder.Build();
            using var pInputAssembly = _inputAssemblyBuilder.Build();
            using var pMultisample = _multisampleStateBuilder.Build();
            using var pRasterizer = _rasterizerBuilder.Build();
            using var pVpState = _viewportStateBuilder?.Build();

            // Ensure the SType is correctly set
            _depthStencilState.SType = StructureType.PipelineDepthStencilStateCreateInfo;

            var pDepthState = CreateMemoryHandle(_depthStencilState);

            GraphicsPipelineCreateInfo ci = new GraphicsPipelineCreateInfo()
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = (uint)_pipelineStageBuilder.Count,
                PStages = (PipelineShaderStageCreateInfo*)pstages.Pointer,
                PVertexInputState = (PipelineVertexInputStateCreateInfo*)pInputState.Pointer,
                PColorBlendState = (PipelineColorBlendStateCreateInfo*)pColorBlend.Pointer,
                PDynamicState = (PipelineDynamicStateCreateInfo*)pDynamicState.Pointer,
                PInputAssemblyState = (PipelineInputAssemblyStateCreateInfo*)pInputAssembly.Pointer,
                PMultisampleState = (PipelineMultisampleStateCreateInfo*)pMultisample.Pointer,
                PRasterizationState = (PipelineRasterizationStateCreateInfo*)pRasterizer.Pointer,
                PViewportState = (PipelineViewportStateCreateInfo*)pVpState.Value.Pointer,
                PDepthStencilState = (PipelineDepthStencilStateCreateInfo*)pDepthState.Pointer,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = _subpassMetadata.Order,
            };

            return VkPipeline.Create(_context, _name, ref ci, (VkRenderPass)_renderPass, _pipelineLayout);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _entryPoint.Dispose();
            }
        }
    }
}