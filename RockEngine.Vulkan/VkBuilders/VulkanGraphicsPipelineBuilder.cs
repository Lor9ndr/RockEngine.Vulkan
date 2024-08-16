using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Buffers;
using System.Text;

namespace RockEngine.Vulkan.VkBuilders
{
    public class GraphicsPipelineBuilder : DisposableBuilder
    {
        private MemoryHandle _entryPoint;
        private readonly VulkanContext _context;
        private readonly PipelineManager _pipelineManager;
        private readonly string _name;
        private RenderPassWrapper _renderPass;
        private PipelineLayoutWrapper _pipelineLayout;
        private PipelineStageBuilder _pipelineStageBuilder = new PipelineStageBuilder();
        private VulkanPipelineVertexInputStateBuilder _vertexInputStateBuilder;
        private VulkanInputAssemblyBuilder _inputAssemblyBuilder;
        private VulkanViewportStateInfoBuilder _viewportStateBuilder;
        private VulkanRasterizerBuilder _rasterizerBuilder;
        private VulkanMultisampleStateInfoBuilder _multisampleStateBuilder;
        private VulkanColorBlendStateBuilder _colorBlendStateBuilder;
        private PipelineDynamicStateBuilder _dynamicStateBuilder;
        private PipelineDepthStencilStateCreateInfo _depthStencilState;

        public GraphicsPipelineBuilder(VulkanContext context, PipelineManager pipelineManager, string name)
        {
            _entryPoint = CreateMemoryHandle(Encoding.ASCII.GetBytes("main"));
            _context = context;
            _pipelineManager = pipelineManager;
            _name = name;
        }

        public GraphicsPipelineBuilder AddRenderPass(RenderPassWrapper renderPass)
        {
            _renderPass = renderPass;
            return this;
        }

        public unsafe GraphicsPipelineBuilder WithShaderModule(ShaderModuleWrapper shaderModule)
        {
            _pipelineStageBuilder.AddStage(shaderModule.Stage, shaderModule, (byte*)_entryPoint.Pointer);
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

        public GraphicsPipelineBuilder WithPipelineLayout(PipelineLayoutWrapper pipelineLayout)
        {
            _pipelineLayout = pipelineLayout;
            return this;
        }

        public GraphicsPipelineBuilder AddDepthStencilState(PipelineDepthStencilStateCreateInfo pipelineDepthStencilStateCreateInfo)
        {
            _depthStencilState = pipelineDepthStencilStateCreateInfo;
            return this;
        }



        /// <summary>
        /// Building the whole pipeline
        /// after finishing disposing layout and all the builders that are sended into it
        /// so you have not dispose them after all.
        /// Also adds the pipeline to the <see cref="VulkanContext.PipelineManager"/> by <see cref="PipelineManager.CreatePipeline(VulkanContext, string, DescriptorPoolSize[], uint, ref GraphicsPipelineCreateInfo, RenderPassWrapper, PipelineLayoutWrapper)"/>
        /// </summary>
        /// <returns>disposable pipeline wrapper</returns>
        public unsafe PipelineWrapper Build()
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
            using var pVpState = _viewportStateBuilder.Build();

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
                PViewportState = (PipelineViewportStateCreateInfo*)pVpState.Pointer,
                PDepthStencilState = (PipelineDepthStencilStateCreateInfo*)pDepthState.Pointer,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
            };

            return _pipelineManager.CreatePipeline(_name, ref ci, _renderPass, _pipelineLayout);
        }
    }
}