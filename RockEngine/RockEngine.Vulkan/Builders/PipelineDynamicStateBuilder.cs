using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.Builders
{
    public class PipelineDynamicStateBuilder : DisposableBuilder
    {
        private List<DynamicState> _dynamicStates = new List<DynamicState>();

        public PipelineDynamicStateBuilder AddState(DynamicState state)
        {
            _dynamicStates.Add(state);
            return this;
        }

        public unsafe MemoryHandle Build()
        {
            fixed (DynamicState* pDynamicStates = _dynamicStates.ToArray())
            {
                return CreateMemoryHandle(new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    PNext = null,
                    Flags = 0,
                    DynamicStateCount = (uint)_dynamicStates.Count,
                    PDynamicStates = pDynamicStates
                });
            }
        }
    }
}
