using RockEngine.ShaderPreprocessor;
using RockEngine.Vulkan;

namespace RockEngine.Core.CoreObjects
{
    public class PipelineLayout : IDisposable
    {
        public VkPipelineLayout VkPipelineLayout { get; }
        public IReadOnlyList<IShaderMetadata?> ShadersMetadata { get; set; }

        public PipelineLayout(VkPipelineLayout layout, params Shader[] shaders)
        {
            VkPipelineLayout = layout;
            ShadersMetadata = shaders.Select(static s => s.Metadata).ToList().AsReadOnly();
        }

        public PipelineLayout(VulkanContext context, params Shader[] shaders) 
            : this(VkPipelineLayout.Create(context, shaders.Select(s => s.ShaderModule).ToArray()), shaders)
        {
        }

        public void Dispose()
        {
            VkPipelineLayout.Dispose();
        }
    }
}
