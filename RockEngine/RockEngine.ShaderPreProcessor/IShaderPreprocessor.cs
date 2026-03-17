using System.Collections.Generic;
using System.Threading.Tasks;

namespace RockEngine.ShaderPreprocessor
{
    public class ShaderPreProcessResult
    {
        public string ProcessedSource { get;}
        public List<LineMapping> LineMappings { get; }
        public ShaderPreProcessResult(string processedSource, List<LineMapping> lineMappings)
        {
            ProcessedSource = processedSource;
            LineMappings = lineMappings;
        }
    }
    public interface IShaderPreprocessor
    {
        /// <summary>
        /// Preprocesses a shader source: processes #include and [MATERIAL] annotations.
        /// </summary>
        /// <param name="source">Raw shader source.</param>
        /// <param name="filePath">Path of the original file (for include resolution).</param>
        /// <param name="defines">List of preprocessor defines (e.g., "BINDLESS_SUPPORTED").</param>
        /// <returns>Processed shader source.</returns>
        Task<ShaderPreProcessResult> PreprocessAsync(string source, string filePath, IReadOnlyList<string> defines = null);
    }
}