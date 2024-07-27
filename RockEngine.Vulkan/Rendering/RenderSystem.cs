using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.Rendering
{
    public abstract class RenderSystem : IDisposable
    {
        protected readonly VulkanContext _context;
        protected readonly RenderPassWrapper _renderPass;
        protected PipelineWrapper? _pipeline;
        protected PipelineLayoutWrapper? _pipelineLayout;

        protected RenderSystem(VulkanContext context, RenderPassWrapper renderPass)
        {
            _context = context;
            _renderPass = renderPass;
        }

        public abstract Task Init(CancellationToken cancellationToken = default);
        protected abstract Task CreateGraphicsPipeline(CancellationToken cancellationToken = default);
        public abstract Task RenderAsync(Project p, CommandBufferWrapper commandBuffer, int frameIndex);
        public abstract void Dispose();
        
    }
}