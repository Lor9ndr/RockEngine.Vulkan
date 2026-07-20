using System.Threading.Tasks;

namespace RockEngine.ShaderPreProcessor
{
    public interface IShaderPreprocessorStep
    {
        Task ExecuteAsync(ShaderPreprocessingContext context);
    }
}