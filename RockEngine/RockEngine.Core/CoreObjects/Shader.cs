using RockEngine.Core.Rendering.Managers;
using RockEngine.ShaderPreprocessor;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.CoreObjects
{
    public class Shader : IDisposable
    {
        public VkShaderModule ShaderModule { get; }
        public IShaderMetadata? Metadata { get; }

        public Shader(VulkanContext context, IShaderCompileResult compileResult)
        {
            Metadata = compileResult.Metadata;
            ShaderModule = VkShaderModule.Create(context, compileResult.ShaderPath, GetVulkanStage());
        }

        public Shader(VulkanContext context, ISpirVShader shader)
        {
            Metadata = shader.Metadata;
            ShaderModule = VkShaderModule.Create(context, shader.ShaderData, GetVulkanStage());
        }

        public ShaderStageFlags GetVulkanStage()
        {
            if(Metadata is null)
            {
                throw new ArgumentNullException(nameof(Metadata));
            }
            return Metadata.Stage switch
            {
                ShaderStage.All => ShaderStageFlags.All,
                ShaderStage.Vertex => ShaderStageFlags.VertexBit,
                ShaderStage.Fragment => ShaderStageFlags.FragmentBit,
                ShaderStage.Geometry => ShaderStageFlags.GeometryBit,
                ShaderStage.Compute => ShaderStageFlags.ComputeBit,
                _ => throw new NotImplementedException(),
            };
        }

        public void Dispose()
        {
            ShaderModule.Dispose();
        }
    }
}
