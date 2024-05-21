using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

using System.Buffers;
using System.Text;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class GraphicsPipelineBuilder:DisposableBuilder
    {
        private MemoryHandle _entryPoint;
        private readonly Vk _vk;
        private readonly VulkanLogicalDevice _device;
        private readonly VulkanRenderPass _renderPass;
        private VulkanPipelineLayout _pipelineLayout;
        private PipelineStageBuilder _pipelineStageBuilder = new PipelineStageBuilder();
        private VulkanPipelineVertexInputStateBuilder _vertexInputStateBuilder;
        private VulkanInputAssemblyBuilder _inputAssemblyBuilder;
        private VulkanViewportStateInfoBuilder _viewportStateBuilder;
        private VulkanRasterizerBuilder _rasterizerBuilder;
        private VulkanMultisampleStateInfoBuilder _multisampleStateBuilder;
        private VulkanColorBlendStateBuilder _colorBlendStateBuilder;
        private VulkanDynamicStateBuilder _dynamicStateBuilder;

        public GraphicsPipelineBuilder(Vk vk, VulkanLogicalDevice device, VulkanRenderPass renderPass)
        {
            _entryPoint = CreateMemoryHandle(Encoding.ASCII.GetBytes("main"));
            _vk = vk;
            _device = device;
            _renderPass = renderPass;
        }

        public unsafe GraphicsPipelineBuilder WithShaderModule(ShaderStageFlags stage, VulkanShaderModule shaderModule)
        {
            _pipelineStageBuilder.AddStage(stage, shaderModule, (byte*)_entryPoint.Pointer);
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

        public GraphicsPipelineBuilder WithDynamicState(VulkanDynamicStateBuilder dynamicStateBuilder)
        {
            _dynamicStateBuilder = dynamicStateBuilder;
            return this;
        }

        public GraphicsPipelineBuilder WithPipelineLayout(VulkanPipelineLayout pipelineLayout)
        {
            _pipelineLayout = pipelineLayout;
            return this;
        }

        /// <summary>
        /// Building the whole pipeline
        /// after finishing disposing layout and all the builders that are sended into it
        /// so you have not dispose them after all
        /// </summary>
        /// <returns>disposable pipeline wrapper</returns>
        public unsafe VulkanPipeline Build()
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
            var pvpState = _viewportStateBuilder.Build();

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
                PViewportState = (PipelineViewportStateCreateInfo*)pvpState.Pointer,
                Layout = _pipelineLayout.Layout,
                RenderPass = _renderPass.RenderPass,
                Subpass = 0,
            };
            try
            {
                _vk.CreateGraphicsPipelines(_device.Device, default, 1, in ci, null, out Pipeline pipeline)
                    .ThrowCode("Failed to create pipeline");
                return new VulkanPipeline(_vk, _device, pipeline); // Placeholder return, replace with actual pipeline creation result
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
                _pipelineLayout.Dispose();
            }
        }
    }
}