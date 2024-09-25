using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.Builders
{
    public class VulkanInputAssemblyBuilder : DisposableBuilder
    {
        private Bool32 _primRestart;
        private PrimitiveTopology _topology;

        public VulkanInputAssemblyBuilder Configure(Bool32 primRestartEnable = default, PrimitiveTopology topology = PrimitiveTopology.TriangleList)
        {
            _primRestart = primRestartEnable;
            _topology = topology;
            return this;
        }
        public MemoryHandle Build()
        {
            return CreateMemoryHandle([new PipelineInputAssemblyStateCreateInfo()
            {
                 SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                 PrimitiveRestartEnable = _primRestart,
                 Topology = _topology
            }]);
        }
    }
}
