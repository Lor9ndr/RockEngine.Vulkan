using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Materials
{
    public interface IShaderReflectionProvider
    {
        MergedShaderReflectionData GetPipelineReflection(VkPipeline pipeline);
        ShaderReflectionData CombineShaderReflections(IEnumerable<ShaderReflectionData> reflections);
    }
}