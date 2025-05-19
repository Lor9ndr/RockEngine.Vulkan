using Silk.NET.Vulkan;

using System.Buffers;
using System.Linq;
using System.Text;

namespace RockEngine.Vulkan.Builders
{
    public class GraphicsPipelineBuilder : DisposableBuilder
    {
        private MemoryHandle _entryPoint;
        private readonly VulkanContext _context;
        private readonly string _name;
        private VkRenderPass _renderPass;
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
        private uint _subpass;

        public GraphicsPipelineBuilder(VulkanContext context, string name)
        {
            _entryPoint = CreateMemoryHandle(Encoding.ASCII.GetBytes("main"));
            _context = context;
            _name = name;
        }

        public static GraphicsPipelineBuilder CreateDefault(VulkanContext context, string name,params VkShaderModule[] shaders)
        {
              
            return new GraphicsPipelineBuilder(context, name)
                .WithShaderModule(shaders)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder())
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, 1280, 720, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), new Extent2D(1280, 720))))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithPipelineLayout(VkPipelineLayout.Create(context, shaders))
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
        }


        public GraphicsPipelineBuilder AddRenderPass(VkRenderPass renderPass)
        {
            _renderPass = renderPass;
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

        public GraphicsPipelineBuilder WithSubpass(uint subpass)
        {
            _subpass = subpass;
            return this;
        }

        public unsafe VkPipeline Build()
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
                Subpass = _subpass,
            };
            return VkPipeline.Create(_context, _name, ref ci, _renderPass, _pipelineLayout);
        }


    }
}