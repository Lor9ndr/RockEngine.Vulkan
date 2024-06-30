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
        private readonly Dictionary<uint, DescriptorSet[]> _descriptorSets;
        private readonly uint _maxSets;
        private readonly DescriptorPoolSize[] _poolSizes;
        private readonly int _requiredDescriptorSets;


        public PipelineLayoutWrapper Layout => _pipelineLayout;
        public RenderPassWrapper RenderPass => _renderPass;
        public string Name => _name;
        public DescriptorPoolSize[] PoolSizes => _poolSizes;
        public Dictionary<uint, DescriptorSet[]> DescriptorSets => _descriptorSets;

        private DescriptorSetWrapper[] _dummyDescriptors;

        public PipelineWrapper(VulkanContext context, string name, Pipeline pipeline, PipelineLayoutWrapper pipelineLayout, RenderPassWrapper renderPass, DescriptorPoolSize[] poolSizes, uint maxSets)
            : base(pipeline)
        {
            _context = context;
            _pipelineLayout = pipelineLayout;
            _renderPass = renderPass;
            _name = name;
            _descriptorSets = new Dictionary<uint, DescriptorSet[]>((int)maxSets);
            _maxSets = maxSets;
            _poolSizes = poolSizes;
            _requiredDescriptorSets = _pipelineLayout.DescriptorSetLayouts.Length;
            _dummyDescriptors = new DescriptorSetWrapper[_requiredDescriptorSets];
            CreateDummyDescriptorSets();
        }

        public unsafe static PipelineWrapper Create(VulkanContext context, string name, DescriptorPoolSize[] poolSizes, uint maxSets, ref GraphicsPipelineCreateInfo ci, RenderPassWrapper renderPass, PipelineLayoutWrapper layout)
        {
            context.Api.CreateGraphicsPipelines(context.Device, pipelineCache: default, 1, in ci, null, out Pipeline pipeline)
                  .ThrowCode("Failed to create pipeline");
            return new PipelineWrapper(context, name, pipeline, layout, renderPass, poolSizes, maxSets);
        }

        private  void CreateDummyDescriptorSets()
        {
            for (uint i = 0; i < _requiredDescriptorSets; i++)
            {
                var layout = _pipelineLayout.DescriptorSetLayouts[i].DescriptorSetLayout;
                _dummyDescriptors[i] = CreateDescriptorSet(_pipelineLayout.DescriptorSetLayouts[i].SetLocation, layout);
            }
        }
        public unsafe void BindDummyDescriptors(CommandBufferWrapper commandBuffer)
        {
            uint offset = 0;
            fixed(DescriptorSet* pDescSets = _dummyDescriptors.Select(s=>s.DescriptorSet).ToArray())
            {
                _context.Api.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, Layout,0, (uint)_dummyDescriptors.Length, pDescSets, 0, in offset);
            }
        }

        public unsafe DescriptorSetWrapper CreateDescriptorSet(uint location, DescriptorSetLayout descriptorSetLayout)
        {
            var pool = _context.DescriptorPoolFactory.GetOrCreatePool(_maxSets, PoolSizes);
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
            if (!_descriptorSets.ContainsKey(location))
            {
                _descriptorSets[location] = new DescriptorSet[_maxSets / (_descriptorSets.Keys.Count + 1)];
            }

            var descSets = _descriptorSets[location];
            for (int i = 0; i < descSets.Length; i++)
            {
                if (descSets[i].Handle == default) // Find not setted 
                {
                    descSets[i] = descriptorSet;
                    return new DescriptorSetWrapper(descriptorSet,location,false);
                }
            }
            throw new Exception("Preset in Pipeline more descriptor sets");
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