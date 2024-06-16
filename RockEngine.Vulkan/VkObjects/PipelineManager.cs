using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class PipelineManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly Dictionary<string, PipelineWrapper> _pipelines;

        public PipelineWrapper? CurrentPipeline;
        
        public event Action<PipelineWrapper>? PipelineCreated;


        public PipelineManager(VulkanContext context)
        {
            _context = context;
            _pipelines = new Dictionary<string, PipelineWrapper>();
        }

        public PipelineWrapper CreatePipeline(VulkanContext context, string name, ref GraphicsPipelineCreateInfo ci, RenderPassWrapper renderPass, PipelineLayoutWrapper layout)
        {
            var pipeline = PipelineWrapper.Create(context, name, ref ci, renderPass, layout);
            _pipelines[name] = pipeline;
            CurrentPipeline = pipeline;
            PipelineCreated?.Invoke(pipeline);
            return pipeline;
        }

        public PipelineWrapper AddPipeline(PipelineWrapper pipeline)
        {
            _pipelines[pipeline.Name] = pipeline;
            PipelineCreated?.Invoke(pipeline);
            return pipeline;
        }

        public PipelineWrapper GetPipeline(string name)
        {
            return _pipelines[name];
        }

        public IEnumerable<PipelineWrapper> GetAllPipelines()
        {
            return _pipelines.Values;
        }

        public unsafe void Dispose()
        {
            foreach (var pipeline in _pipelines.Values)
            {
                pipeline.Dispose();
            }
        }
    }
}
