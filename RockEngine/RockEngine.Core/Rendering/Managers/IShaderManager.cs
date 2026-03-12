namespace RockEngine.Core.Rendering.Managers
{
    public interface IShaderManager
    {
        Task CompileAllShadersAsync();
        Task<string> CompileShader(string path);
        byte[] GetShader(string name, bool removeAfterGet = true);
    }
}