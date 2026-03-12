using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Materials
{
    public interface IShaderReflectionProvider
    {
        ShaderReflectionData GetPipelineReflection(VkPipeline pipeline);
        ShaderReflectionData CombineShaderReflections(IEnumerable<ShaderReflectionData> reflections);
    }
}