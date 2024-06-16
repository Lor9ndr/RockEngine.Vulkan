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
        private Dictionary<string, DescriptorSet> _descriptorSets;
        private readonly uint _requiredDescriptorSets;
        private readonly List<WriteDescriptorSet> _writeDescriptorSets = new List<WriteDescriptorSet>();

        public PipelineLayoutWrapper Layout => _pipelineLayout;
        public RenderPassWrapper RenderPass => _renderPass;
        public string Name => _name;
        public uint RequiredDescriptorSets => _requiredDescriptorSets;
        public IReadOnlyDictionary<string, DescriptorSet> DescriptorSets => _descriptorSets;

        public PipelineWrapper(VulkanContext context, string name, Pipeline pipeline, PipelineLayoutWrapper pipelineLayout, RenderPassWrapper renderPass)
            :base(pipeline)
        {
            _context = context;
            _pipelineLayout = pipelineLayout;
            _renderPass = renderPass;
            _name = name;
            _requiredDescriptorSets = (uint)pipelineLayout.DescriptorSetLayouts.Length;
            _descriptorSets = new Dictionary<string, DescriptorSet>((int)RequiredDescriptorSets);
        }

        public unsafe static PipelineWrapper Create(VulkanContext context, string name, ref GraphicsPipelineCreateInfo ci, RenderPassWrapper renderPass, PipelineLayoutWrapper layout)
        {
            context.Api.CreateGraphicsPipelines(context.Device, pipelineCache: default, 1, in ci, null, out Pipeline pipeline)
                  .ThrowCode("Failed to create pipeline");

            return new PipelineWrapper(context, name, pipeline, layout, renderPass);
        }

        public unsafe bool SetBuffer(UniformBufferObject ubo, uint bindingIndex)
        {
            if (!_descriptorSets.TryGetValue(ubo.Name, out var descriptorSet))
            {
                return false;
            }

            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = ubo.UniformBuffer,
                Offset = 0,
                Range = ubo.Size
            };

            var writeDescriptorSet = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = bindingIndex,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };
            _writeDescriptorSets.Add(writeDescriptorSet);
            var currentDescriptros = _writeDescriptorSets.ToArray();
            fixed (WriteDescriptorSet* descSet = currentDescriptros)
            {
                _context.QueueMutex.WaitOne();
                try
                {
                    _context.Api.UpdateDescriptorSets(_context.Device, (uint)currentDescriptros.Length, descSet, 0, null);
                }
                finally
                {
                    _context.QueueMutex.ReleaseMutex();
                }
            }

            return true;
        }
        public unsafe DescriptorSet CreateDescriptorSet(string setName, DescriptorPoolWrapper descriptorPool, DescriptorSetLayout descriptorSetLayout)
        {
            if (_descriptorSets.ContainsKey(setName))
            {
                throw new InvalidOperationException($"Descriptor set with name {setName} already exists.");
            }

            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &descriptorSetLayout
            };

            DescriptorSet descriptorSet;
            _context.Api.AllocateDescriptorSets(_context.Device, &allocInfo, &descriptorSet)
                  .ThrowCode("Failed to allocate descriptor set");

            _descriptorSets[setName] = descriptorSet;
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