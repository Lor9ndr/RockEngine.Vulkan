using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.GUI;
using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Diagnostics;

namespace RockEngine.Vulkan.Rendering
{
    internal class SceneRenderSystem : RenderSystem
    {
        public SceneRenderSystem(VulkanContext context, RenderPassWrapper renderPass)
            :base(context, renderPass)
        {
        }

        public override Task Init(CancellationToken cancellationToken = default)
        {
            return CreateGraphicsPipeline(cancellationToken);
        }

        protected override async Task CreateGraphicsPipeline(CancellationToken cancellationToken = default)
        {
            // Load the vertex shader
            using var vertexShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Shaders\\Shader.vert.spv", ShaderStageFlags.VertexBit, cancellationToken)
                .ConfigureAwait(false);

            // Load the fragment shader
            using var fragmentShaderModule = await ShaderModuleWrapper.CreateAsync(_context, "..\\..\\..\\Shaders\\Shader.frag.spv", ShaderStageFlags.FragmentBit, cancellationToken)
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
            var poolSize = new DescriptorPoolSize()
            {
                DescriptorCount = 10000,
                Type = DescriptorType.UniformBuffer
            };
            var poolSamplerSize = new DescriptorPoolSize()
            {
                DescriptorCount = 10000,
                Type = DescriptorType.CombinedImageSampler
            };
            // Create Uniform Buffers
            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(_context, "Base")
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
                   .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
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
               })
               .AddPoolSizes([poolSize, poolSamplerSize])
               .SetMaxSets(200);
            _pipeline = pBuilder.Build();
        }

        public  override async Task RenderAsync(Project p, CommandBufferWrapper commandBuffer, int frameIndex)
        {
            Debug.Assert(_pipeline != null, "Has to create graphics pipeline before rendering");
            Debug.Assert(_pipelineLayout != null, "Has to create graphics pipeline before rendering");

            _context.Api.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
            //_pipeline.BindDummyDescriptors(commandBuffer);
            //unsafe
            //{
            //    var descriptors = _pipeline.DescriptorSets.Values.ToArray();
            //    fixed (DescriptorSet* pDescriptors = descriptors)
            //        _context.Api.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, (uint)descriptors.Length, pDescriptors, null);
            //}

            _context.PipelineManager.CurrentPipeline = _pipeline;

           await p.CurrentScene.RenderAsync(_context, commandBuffer);


        }

        public override void Dispose()
        {
            _pipelineLayout?.Dispose();
            _pipeline?.Dispose();
        }
    }
}