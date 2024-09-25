using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.Builders
{
    public class VulkanMultisampleStateInfoBuilder : DisposableBuilder
    {
        private Bool32 _sampleShading;
        private SampleCountFlags _rasterSamples;

        public VulkanMultisampleStateInfoBuilder Configure(Bool32 sampleShadingEnable, SampleCountFlags rasterSamples)
        {
            _sampleShading = sampleShadingEnable;
            _rasterSamples = rasterSamples;
            return this;
        }
        public MemoryHandle Build()
        {
            return CreateMemoryHandle([new PipelineMultisampleStateCreateInfo()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = _sampleShading,
                RasterizationSamples = _rasterSamples
            }]);
        }
    }
}
