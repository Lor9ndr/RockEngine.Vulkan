using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class PipelineStageBuilder:DisposableBuilder
    {
        private readonly List<PipelineShaderStageCreateInfo> _stages = [];
        public int Count => _stages.Count;
        
        /// <summary>
        /// Adds shader stage to the list
        /// </summary>
        /// <param name="stage"></param>
        /// <param name="module"></param>
        /// <param name="entryPoint"></param>
        /// <returns>chaining</returns>
        public unsafe PipelineStageBuilder AddStage(ShaderStageFlags stage, ShaderModuleWrapper module, byte* entryPoint)
        {
            _stages.Add(new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = stage,
                Module = module,
                PName = entryPoint
            });
            return this; // Return the builder for chaining
        }
        
        public MemoryHandle Build() => CreateMemoryHandle(_stages.ToArray());
    }
}
