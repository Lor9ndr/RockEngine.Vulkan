using RockEngine.Core.ECS.Components;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class PipelineManager : IDisposable
    {
        private readonly RenderingContext _context;
        private readonly List<VkPipeline> _pipelines = new List<VkPipeline>();

        public PipelineManager(RenderingContext context)
        {
            _context = context;
        }

        public VkPipeline Create(GraphicsPipelineBuilder builder)
        {
            var pipeline = builder.Build();
            _pipelines.Add(pipeline);
            return pipeline;
        }

        public VkPipeline Create(string name, ref GraphicsPipelineCreateInfo info, VkRenderPass renderPass, VkPipelineLayout layout)
        {
            var pipeline = VkPipeline.Create(_context, name, ref info, renderPass, layout);
            _pipelines.Add(pipeline);
            return pipeline;
        }

        public VkDescriptorSetLayout TryGetLayout(UniformBuffer uniformBuffer)
        {
            return _pipelines.SelectMany(
                s => s.Layout.DescriptorSetLayouts.Where(
                    layout => layout.Value.Bindings.FirstOrDefault(
                        s => s.Name == uniformBuffer.Name && s.Binding == uniformBuffer.BindingLocation) != null))
                                    .FirstOrDefault().Value;
        }

        public VkDescriptorSetLayout TryGetLayout(in Material material)
        {
            return material.Pipeline.Layout.DescriptorSetLayouts[2];
        }

        public VkPipeline? GetPipelineByName(string name)
        {
            return _pipelines.FirstOrDefault(p => p.Name == name);
        }

        public void Dispose()
        {
            foreach (var item in _pipelines)
            {
                item.Dispose();
            }
        }
    }
}
