using RockEngine.ShaderPreprocessor;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{

    public interface IShaderCompileResult
    {
        public string ShaderPath { get; set; }
        public ShaderMetadata Metadata { get; set; }
    }

    public interface ISpirVShader
    {
        public byte[] ShaderData { get; set; }
        public ShaderMetadata Metadata { get; set; }
    }

    public interface IShaderManager
    {
        Task CompileAllShadersAsync();
        Task<IShaderCompileResult> CompileShaderAsync(string path);
        Task<IShaderCompileResult> CompileShaderByStringAsync(string shader, ShaderStageFlags stage);
        ISpirVShader GetShader(string name, bool removeAfterGet = true);
    }
}