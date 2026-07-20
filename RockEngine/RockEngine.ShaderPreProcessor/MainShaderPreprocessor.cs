using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RockEngine.ShaderPreProcessor;

namespace RockEngine.ShaderPreprocessor
{
    public class MainShaderPreprocessor : IShaderPreprocessor
    {
        private readonly IEnumerable<IShaderPreprocessorStep> _steps;

        public MainShaderPreprocessor(IEnumerable<IShaderPreprocessorStep> steps)
        {
            _steps = steps;
        }

        public async Task<ShaderPreProcessResult> PreprocessAsync(
            string source, string filePath,
            IReadOnlyList<string> defines = null,
            IReadOnlyList<string> extensions = null)
        {
            var context = new ShaderPreprocessingContext(source, filePath, defines, extensions);
            // Determine shader stage from extension (as before)
            context.Metadata.Stage = DetermineStage(filePath);
            foreach (var step in _steps)
            {
                await step.ExecuteAsync(context);
            }

            return new ShaderPreProcessResult(context.Source, context.LineMappings, context.Metadata);
        }
        private ShaderStage DetermineStage(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            return extension switch
            {
                ".vert" => ShaderStage.Vertex,
                ".frag" => ShaderStage.Fragment,
                ".geom" => ShaderStage.Geometry,
                ".comp" => ShaderStage.Compute,
                _ => ShaderStage.All
            };

        }

    }
}