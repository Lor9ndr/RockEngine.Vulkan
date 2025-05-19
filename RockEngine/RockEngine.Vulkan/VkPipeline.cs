using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkPipeline : VkObject<Pipeline>
    {
        private readonly string _name;
        private readonly VulkanContext _context;
        private readonly VkPipelineLayout _pipelineLayout;
        private readonly VkRenderPass? _renderPass;

        public string Name => _name;
        public VkPipelineLayout Layout => _pipelineLayout;
        public VkRenderPass? RenderPass => _renderPass;
        public uint SubPass { get; private set; }

        public VkPipeline(VulkanContext context, string name, Pipeline pipeline, VkPipelineLayout pipelineLayout, VkRenderPass? renderPass, uint subpass)
            : base(pipeline)
        {
            _context = context;
            _pipelineLayout = pipelineLayout;
            _renderPass = renderPass;
            _name = name;
            SubPass = subpass;
        }
        public VkPipeline(VulkanContext context, string name, Pipeline pipeline, VkPipelineLayout pipelineLayout): base(pipeline)
        {
            _context = context;
            _pipelineLayout = pipelineLayout;
            _name = name;
        }

        public static unsafe VkPipeline Create(VulkanContext context, string name, ref GraphicsPipelineCreateInfo ci, VkRenderPass renderPass, VkPipelineLayout layout)
        {
            VulkanContext.Vk.CreateGraphicsPipelines(context.Device, pipelineCache: default, 1, in ci, in VulkanContext.CustomAllocator<VkPipeline>(), out Pipeline pipeline)
                  .VkAssertResult("Failed to create pipeline");
            context.DebugUtils.SetDebugUtilsObjectName(pipeline,ObjectType.Pipeline, name);

            return new VkPipeline(context, name, pipeline, layout, renderPass, ci.Subpass);
        }

        public static VkPipeline CreateComputePipeline(VulkanContext context, string name, VkPipelineLayout layout,in ComputePipelineCreateInfo ci)
        {
            VulkanContext.Vk.CreateComputePipelines(context.Device, default,1u, in ci, in VulkanContext.CustomAllocator<VkPipeline>(),  out Pipeline pipeline);
            return new VkPipeline(context, name, pipeline, layout, null, 0);
        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.Pipeline, name);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                VulkanContext.Vk.DestroyPipeline(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkPipeline>());
                _disposed = true;
            }
        }

       
    }
}