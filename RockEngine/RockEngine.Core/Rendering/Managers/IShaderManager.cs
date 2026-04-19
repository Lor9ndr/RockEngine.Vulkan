using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public interface IShaderManager
    {
        Task CompileAllShadersAsync();
        Task<string> CompileShaderAsync(string path);
        Task<string> CompileShaderByStringAsync(string shader, ShaderStageFlags stage);
        byte[] GetShader(string name, bool removeAfterGet = true);
    }
}