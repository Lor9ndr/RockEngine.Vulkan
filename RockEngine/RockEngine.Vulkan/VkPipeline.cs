using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkPipeline : VkObject<Pipeline>
    {
        private readonly string _name;
        private readonly RenderingContext _context;
        private readonly VkPipelineLayout _pipelineLayout;
        private readonly VkRenderPass _renderPass;

        public string Name => _name;
        public VkPipelineLayout Layout => _pipelineLayout;
        public VkRenderPass RenderPass => _renderPass;

        public VkPipeline(RenderingContext context, string name, Pipeline pipeline, VkPipelineLayout pipelineLayout, VkRenderPass renderPass)
            : base(pipeline)
        {
            _context = context;
            _pipelineLayout = pipelineLayout;
            _renderPass = renderPass;
            _name = name;
        }

        public unsafe static VkPipeline Create(RenderingContext context, string name, ref GraphicsPipelineCreateInfo ci, VkRenderPass renderPass, VkPipelineLayout layout)
        {
            RenderingContext.Vk.CreateGraphicsPipelines(context.Device, pipelineCache: default, 1, in ci, in RenderingContext.CustomAllocator<VkPipeline>(), out Pipeline pipeline)
                  .VkAssertResult("Failed to create pipeline");
            return new VkPipeline(context, name, pipeline, layout, renderPass);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                RenderingContext.Vk.DestroyPipeline(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkPipeline>());
                _disposed = true;
            }
        }
    }
}