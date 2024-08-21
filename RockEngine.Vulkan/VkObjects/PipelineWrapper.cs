using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class PipelineWrapper : VkObject<Pipeline>
    {
        private readonly string _name;
        private readonly VulkanContext _context;
        private readonly PipelineLayoutWrapper _pipelineLayout;
        private readonly RenderPassWrapper _renderPass;

        public string Name => _name;
        public PipelineLayoutWrapper Layout => _pipelineLayout;
        public RenderPassWrapper RenderPass => _renderPass;

        public PipelineWrapper(VulkanContext context, string name, Pipeline pipeline, PipelineLayoutWrapper pipelineLayout, RenderPassWrapper renderPass)
            : base(pipeline)
        {
            _context = context;
            _pipelineLayout = pipelineLayout;
            _renderPass = renderPass;
            _name = name;
        }

        public unsafe static PipelineWrapper Create(VulkanContext context, string name, ref GraphicsPipelineCreateInfo ci, RenderPassWrapper renderPass, PipelineLayoutWrapper layout)
        {
            context.Api.CreateGraphicsPipelines(context.Device, pipelineCache: default, 1, in ci, null, out Pipeline pipeline)
                  .ThrowCode("Failed to create pipeline");
            return new PipelineWrapper(context, name, pipeline, layout, renderPass);
        }

        public unsafe DescriptorSet CreateDescriptorSet(DescriptorSetLayout descriptorSetLayout)
        {
            var pool = _context.DescriptorPoolFactory.GetOrCreatePool();
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = 1,
                PSetLayouts = &descriptorSetLayout
            };

            DescriptorSet descriptorSet;
            _context.Api.AllocateDescriptorSets(_context.Device, &allocInfo, &descriptorSet)
                  .ThrowCode("Failed to allocate descriptor set");

             return descriptorSet;
        }
       

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                unsafe
                {
                    _context.Api.DestroyPipeline(_context.Device, _vkObject, null);
                }
                _disposed = true;
            }
        }
    }
}