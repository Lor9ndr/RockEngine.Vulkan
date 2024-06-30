using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace RockEngine.Vulkan.VkBuilders
{
    public class GraphicsPipelineBuilder : DisposableBuilder
    {
        private MemoryHandle _entryPoint;
        private readonly VulkanContext _context;
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
        private DescriptorPoolSize[] _poolSizes;
        private uint _maxSets;

        public GraphicsPipelineBuilder(VulkanContext context, string name)
        {
            _entryPoint = CreateMemoryHandle(Encoding.ASCII.GetBytes("main"));
            _context = context;
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

        public GraphicsPipelineBuilder AddPoolSizes(DescriptorPoolSize[] poolSizes)
        {
            _poolSizes = poolSizes;
            return this;
        }
        public GraphicsPipelineBuilder SetMaxSets(uint maxSets)
        {
            _maxSets = maxSets;
            return this;
        }


        /// <summary>
        /// Building the whole pipeline
        /// after finishing disposing layout and all the builders that are sended into it
        /// so you have not dispose them after all.
        /// Also adds the pipeline to the <see cref="VulkanContext.PipelineManager"/> by <see cref="PipelineManager.AddPipeline(PipelineWrapper)"/>
        /// </summary>
        /// <returns>disposable pipeline wrapper</returns>
        public unsafe PipelineWrapper Build()
        {
            ArgumentNullException.ThrowIfNull(_pipelineLayout);
            ArgumentNullException.ThrowIfNull(_pipelineStageBuilder);
            ArgumentNullException.ThrowIfNull(_vertexInputStateBuilder);
            ArgumentNullException.ThrowIfNull(_colorBlendStateBuilder);
            ArgumentNullException.ThrowIfNull(_dynamicStateBuilder);
            ArgumentNullException.ThrowIfNull(_inputAssemblyBuilder);
            ArgumentNullException.ThrowIfNull(_multisampleStateBuilder);
            ArgumentNullException.ThrowIfNull(_rasterizerBuilder);
            ArgumentNullException.ThrowIfNull(_viewportStateBuilder);

            var pstages = _pipelineStageBuilder.Build();
            var pInputState = _vertexInputStateBuilder.Build();
            var pColorBlend = _colorBlendStateBuilder.Build();
            var pDynamicState = _dynamicStateBuilder.Build();
            var pInputAssembly = _inputAssemblyBuilder.Build();
            var pMultisample = _multisampleStateBuilder.Build();
            var pRasterizer = _rasterizerBuilder.Build();
            var pVpState = _viewportStateBuilder.Build();

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

            try
            {
                _context.Api.CreateGraphicsPipelines(_context.Device, default, 1, in ci, null, out Pipeline pipeline)
                    .ThrowCode("Failed to create pipeline");

                var pipelineWrapper = new PipelineWrapper(_context, _name, pipeline, _pipelineLayout, _renderPass,_poolSizes, _maxSets);

                _context.PipelineManager.AddPipeline(pipelineWrapper);
                return pipelineWrapper;
            }
            finally
            {
                _pipelineStageBuilder.Dispose();
                _vertexInputStateBuilder.Dispose();
                _colorBlendStateBuilder.Dispose();
                _dynamicStateBuilder.Dispose();
                _inputAssemblyBuilder.Dispose();
                _multisampleStateBuilder.Dispose();
                _rasterizerBuilder.Dispose();
                _viewportStateBuilder.Dispose();
            }
        }

    }
}