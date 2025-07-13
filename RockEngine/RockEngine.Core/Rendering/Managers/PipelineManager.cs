using RockEngine.Core.Assets.Registres;
using RockEngine.Core.Builders;
using RockEngine.Core.Registries;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class PipelineManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly IRegistry<VkPipeline, string> _pipelineRegistry;

        public PipelineManager(VulkanContext context, IRegistry<VkPipeline, string> pipelineRegistry)
        {
            _context = context;
            _pipelineRegistry = pipelineRegistry;
        }

        public VkPipeline Create(GraphicsPipelineBuilder builder)
        {
            var pipeline = builder.Build();
            CheckPipeline(pipeline);
            _pipelineRegistry.Register(pipeline.Name, pipeline);
            return pipeline;
        }
        public VkPipeline Create(ComputePipelineBuilder builder)
        {
            var pipeline = builder.Build();
            CheckPipeline(pipeline);
            _pipelineRegistry.Register(pipeline.Name, pipeline);
            return pipeline;
        }

        public VkPipeline Create(string name, ref GraphicsPipelineCreateInfo info, VkRenderPass renderPass, VkPipelineLayout layout)
        {
            var pipeline = VkPipeline.Create(_context, name, ref info, renderPass, layout);
            CheckPipeline(pipeline);
            _pipelineRegistry.Register(pipeline.Name, pipeline);
            return pipeline;
        }

        private void CheckPipeline(VkPipeline pipeline)
        {
            if (_pipelineRegistry.Get(pipeline.Name) is not null)
            {
                throw new Exception("Pipeline with that name already exists");
            }
        }
        public VkPipeline? GetPipelineByName(string name)
        {
            return _pipelineRegistry.Get(name);
        }

        public void Dispose()
        {
            _pipelineRegistry.Dispose();
        }
    }
}
