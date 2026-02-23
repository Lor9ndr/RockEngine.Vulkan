using RockEngine.Core.Builders;
using RockEngine.Core.Registries;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class PipelineManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly IRegistry<RckPipeline, string> _pipelineRegistry;
        private bool _disposed = false;

        public PipelineManager(VulkanContext context, IRegistry<RckPipeline, string> pipelineRegistry)
        {
            _context = context;
            _pipelineRegistry = pipelineRegistry;
        }

        public RckPipeline Create(GraphicsPipelineBuilder builder)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var pipeline = builder.Build();
            CheckPipeline(pipeline);
            _pipelineRegistry.Register(pipeline.Name, pipeline);
            return pipeline;
        }

        public RckPipeline Create(ComputePipelineBuilder builder)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var pipeline = builder.Build();
            CheckPipeline(pipeline);
            _pipelineRegistry.Register(pipeline.Name, pipeline);
            return pipeline;
        }

        private void CheckPipeline(RckPipeline pipeline)
        {
            if (_pipelineRegistry.Get(pipeline.Name) is not null)
            {
                throw new Exception($"Pipeline with name '{pipeline.Name}' already exists");
            }
        }

        public RckPipeline? GetPipelineByName(string name)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pipelineRegistry.Get(name);
        }

        public RckPipeline? GetPipelineForSubpass(string subpassName)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pipelineRegistry.GetAll().FirstOrDefault(p => p.Type == PipelineType.Graphics && p.SubpassName == subpassName);
        }

        public IEnumerable<RckPipeline> GetPipelinesForRenderPass(RckRenderPass renderPass)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pipelineRegistry.GetAll().Where(p => p.Type == PipelineType.Graphics && p.RenderPass == renderPass);
        }

        public IEnumerable<RckPipeline> GetComputePipelines()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pipelineRegistry.GetAll().Where(p => p.Type == PipelineType.Compute);
        }

        public void RemovePipeline(string name)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pipelineRegistry.Unregister(name);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            // The registry will handle disposal of all pipelines
            _pipelineRegistry.Dispose();
            _disposed = true;
        }
    }
}