using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanDynamicStateBuilder : DisposableBuilder
    {
        public List<DynamicState> _states = new List<DynamicState>();
        public VulkanDynamicStateBuilder AddState(DynamicState state)
        {
            _states.Add(state);
            return this;
        }
        public unsafe MemoryHandle Build()
        {
            return CreateMemoryHandle([new PipelineDynamicStateCreateInfo()
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = (uint)_states.Count,
                PDynamicStates = (DynamicState*)CreateMemoryHandle(_states.ToArray()).Pointer
            }]);
        }
    }
}
