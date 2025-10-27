using RockEngine.Core.Builders;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;

namespace RockEngine.Core.Extensions.Builders
{
    public static class PipelineBuilderExtensions
    {
        public static GraphicsPipelineBuilder WithMeshFormat<TVertex>(
            this GraphicsPipelineBuilder builder)
            where TVertex : unmanaged, IVertex
        {
            return builder.WithVertexInputState(new VulkanPipelineVertexInputStateBuilder().Add(TVertex.GetBindingDescription(), TVertex.GetAttributeDescriptions()));
        }
    }
}
