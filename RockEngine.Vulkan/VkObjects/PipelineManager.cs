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
        public unsafe void SetBuffer(UniformBufferObject ubo, uint setIndex, uint bindingIndex)
        {
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = ubo.UniformBuffer,
                Offset = 0,
                Range = ubo.Size
            };

            // Find the correct descriptor set layout
            var matchingPipelines = _pipelines.Where(s=> s.Value.Layout.DescriptorSetLayouts
                .Any(s => s.SetLocation == setIndex && s.Bindings.Any(b => b.DescriptorType == DescriptorType.UniformBuffer && b.Binding == bindingIndex)));

            if (!matchingPipelines.Any())
            {
                throw new InvalidOperationException("No matching descriptor set layout found for the given set index and binding index.");
            }
            _context.QueueMutex.WaitOne();
            try
            {
                foreach (var matchingPipeline in matchingPipelines)
                {
                    var pipeline = matchingPipeline.Value;
                    var layout = pipeline.Layout.DescriptorSetLayouts.FirstOrDefault(s => s.SetLocation == setIndex &&
                                                                            s.Bindings.Any(b => b.DescriptorType == DescriptorType.UniformBuffer && b.Binding == bindingIndex));
                    var writeDescriptorSet = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = pipeline.DescriptorSets[layout.SetLocation],
                        DstBinding = bindingIndex,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.UniformBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &bufferInfo
                    };
                    _context.Api.UpdateDescriptorSets(_context.Device, 1, &writeDescriptorSet, 0, null);
                }
            }
            finally
            {
                _context.QueueMutex.ReleaseMutex();
            }
           
           
        }
        public unsafe void SetTexture(Texture texture, uint setIndex, uint bindingIndex)
        {
            var imageInfo = new DescriptorImageInfo
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = texture.ImageView,
                Sampler = texture.Sampler,
            };

            // Find the correct descriptor set layout
            var matchingPipelines = _pipelines.Where(s => s.Value.Layout.DescriptorSetLayouts
                .Any(s => s.SetLocation == setIndex && s.Bindings.Any(b => b.DescriptorType == DescriptorType.CombinedImageSampler && b.Binding == bindingIndex)));

            if (!matchingPipelines.Any())
            {
                throw new InvalidOperationException("No matching descriptor set layout found for the given set index and binding index.");
            }
            foreach (var matchingPipeline in matchingPipelines)
            {
                var pipeline = matchingPipeline.Value;
                var layout = pipeline.Layout.DescriptorSetLayouts.FirstOrDefault(s => s.SetLocation == setIndex &&
                                                                        s.Bindings.Any(b => b.DescriptorType == DescriptorType.CombinedImageSampler && b.Binding == bindingIndex));
                var writeDescriptorSet = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = pipeline.DescriptorSets[layout.SetLocation],
                    DstBinding = bindingIndex,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfo
                };
                _context.Api.UpdateDescriptorSets(_context.Device, 1, &writeDescriptorSet, 0, null);

            }
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
