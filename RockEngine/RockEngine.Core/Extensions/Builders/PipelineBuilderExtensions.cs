using RockEngine.Core.Builders;

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
