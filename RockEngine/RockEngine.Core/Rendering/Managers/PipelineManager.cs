using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class PipelineManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly Dictionary<string, VkPipeline> _pipelines = new Dictionary<string, VkPipeline>();

        public PipelineManager(VulkanContext context)
        {
            _context = context;
        }

        public VkPipeline Create(GraphicsPipelineBuilder builder)
        {
            var pipeline = builder.Build();
            CheckPipeline(pipeline);
            _pipelines[pipeline.Name] = pipeline;
            return pipeline;
        }
        public VkPipeline Create(ComputePipelineBuilder builder)
        {
            var pipeline = builder.Build();
            CheckPipeline(pipeline);
            _pipelines[pipeline.Name] = pipeline;
            return pipeline;
        }

        public VkPipeline Create(string name, ref GraphicsPipelineCreateInfo info, VkRenderPass renderPass, VkPipelineLayout layout)
        {
            var pipeline = VkPipeline.Create(_context, name, ref info, renderPass, layout);
            CheckPipeline(pipeline);
            _pipelines[pipeline.Name] = pipeline;
            return pipeline;
        }

        private void CheckPipeline(VkPipeline pipeline)
        {
            if (_pipelines.ContainsKey(pipeline.Name))
            {
                throw new Exception("Pipeline with that name already exists");
            }
        }
        public VkPipeline GetPipelineByName(string name)
        {
            return _pipelines[name];
        }

        public void Dispose()
        {
            foreach (var item in _pipelines)
            {
                item.Value.Dispose();
            }
        }
    }
}
