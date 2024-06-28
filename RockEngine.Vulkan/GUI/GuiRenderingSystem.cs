using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.GUI
{
    internal class GuiRenderingSystem : RenderSystem
    {
        private readonly List<GuiElement> _elements = new List<GuiElement>();
        private BufferWrapper _buffer;

        public GuiRenderingSystem(VulkanContext context, RenderPassWrapper renderPass) 
            : base(context, renderPass)
        {
        }

        public void AddElement(GuiElement element)
        {
            _elements.Add(element);
        }

        public override Task Init(CancellationToken cancellationToken = default)
        {
            var bufferCreateInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = 1024 * 1024,
                Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive
            };

            _buffer = BufferWrapper.Create(_context, bufferCreateInfo, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            return CreateGraphicsPipeline(cancellationToken);
        }

        protected override async Task CreateGraphicsPipeline(CancellationToken cancellationToken = default)
        {
            // Load the vertex shader
            using var vertexShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Shaders\\Gui.vert.spv", ShaderStageFlags.VertexBit, cancellationToken)
                .ConfigureAwait(false);

            // Load the fragment shader
            using var fragmentShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Shaders\\Gui.frag.spv", ShaderStageFlags.FragmentBit, cancellationToken)
                .ConfigureAwait(false);

            PipelineColorBlendAttachmentState colorBlendAttachmentState = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.RBit |
               ColorComponentFlags.GBit |
               ColorComponentFlags.BBit |
               ColorComponentFlags.ABit
            };


            // Create the pipeline layout with both descriptor set layouts
            _pipelineLayout = PipelineLayoutWrapper.Create(_context, vertexShaderModule, fragmentShaderModule);

            // Create Uniform Buffers
            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(_context, "Gui")
               .AddRenderPass(_renderPass)
               .WithPipelineLayout(_pipelineLayout)
               .WithDynamicState(new PipelineDynamicStateBuilder()
                   .AddState(DynamicState.Viewport)
                   .AddState(DynamicState.Scissor))
               .WithMultisampleState(new VulkanMultisampleStateInfoBuilder()
                   .Configure(false, SampleCountFlags.Count1Bit))
               .WithRasterizer(new VulkanRasterizerBuilder())
               .WithColorBlendState(new VulkanColorBlendStateBuilder()
                   .Configure(LogicOp.Copy)
                   .AddAttachment(colorBlendAttachmentState))
               .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
               .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                   .Add(GuiVertex.GetBindingDescription(), GuiVertex.GetAttributeDescriptions()))
               .WithShaderModule(vertexShaderModule)
               .WithShaderModule(fragmentShaderModule)
               .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport())
                    .AddScissors(new Rect2D()))
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
                   Front = new StencilOpState(),
                   Back = new StencilOpState()
               });

            _pipeline = pBuilder.Build();

            var poolSize = new DescriptorPoolSize()
            {
                DescriptorCount = 2,
                Type = DescriptorType.UniformBuffer
            };
            var poolSamplerSize = new DescriptorPoolSize()
            {
                DescriptorCount = 2,
                Type = DescriptorType.CombinedImageSampler
            };

            var descPool = _context.DescriptorPoolFactory.GetOrCreatePool(4, [poolSize, poolSamplerSize]);
            _pipeline.AutoCreateDescriptorSets(descPool);
        }

        public override async Task RenderAsync(Project p, CommandBufferWrapper commandBuffer, uint frameIndex)
        {
            _context.Api.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
            unsafe
            {
                var descriptors = _pipeline.DescriptorSets.Values.ToArray();
                if (descriptors.Length > 0)
                {
                    fixed (DescriptorSet* pDescriptors = descriptors)
                        _context.Api.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, (uint)descriptors.Length, pDescriptors, null);
                }

                   
            }
            _context.PipelineManager.CurrentPipeline = _pipeline;
            foreach (var item in _elements)
            {
                await item.UpdateBuffer(_context, _buffer);
                await item.Render(_context, commandBuffer,_buffer);
            }
        }

        
        public override void Dispose()
        {
            _pipelineLayout?.Dispose();
            _pipeline?.Dispose();
            _buffer.Dispose();
        }

    }
}
