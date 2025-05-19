using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.Builders
{
    public class PipelineDynamicStateBuilder : DisposableBuilder
    {
        private readonly List<DynamicState> _dynamicStates = new List<DynamicState>();

        public PipelineDynamicStateBuilder AddState(DynamicState state)
        {
            _dynamicStates.Add(state);
            return this;
        }

        public unsafe MemoryHandle Build()
        {
            if (_dynamicStates.Count == 0)
            {
                return new MemoryHandle();
            }
            var ptr = CreateMemoryHandle(_dynamicStates.ToArray());
            return CreateMemoryHandle(new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                PNext = null,
                Flags = 0,
                DynamicStateCount = (uint)_dynamicStates.Count,
                PDynamicStates = (DynamicState*)(_dynamicStates.Count > 0 ? ptr.Pointer : null)
            });
        }
    }
}
