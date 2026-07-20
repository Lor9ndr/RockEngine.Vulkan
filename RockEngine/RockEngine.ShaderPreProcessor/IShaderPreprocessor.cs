using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RockEngine.ShaderPreprocessor
{
    public enum ShaderStage
    {
        All = 0,
        Vertex = 1,
        Fragment = 2,
        Geometry = 3,
        Compute = 4, 
    }
    public interface IShaderMetadata
    {
        public IReadOnlyList<TextureInfo> Textures { get; }
        public ShaderStage Stage { get; }
    }

    public class ShaderMetadata : IShaderMetadata
    {
        public IReadOnlyList<TextureInfo> Textures { get; private set; }
        public ShaderStage Stage { get; set; }

        // Parameterless constructor for initial creation
        public ShaderMetadata()
        {
            Textures = new List<TextureInfo>();
            Stage = ShaderStage.All;
        }

        // Constructor that sets everything at once (still useful)
        public ShaderMetadata(IReadOnlyList<TextureInfo> textures, ShaderStage stage)
        {
            Textures = textures;
            Stage = stage;
        }

        // Helper to add textures (e.g. from annotation handler)
        public void AddTextures(IEnumerable<TextureInfo> textures)
        {
            var currentList = Textures as List<TextureInfo> ?? Textures.ToList();
            currentList.AddRange(textures);
            Textures = currentList;
        }
    }

    public class TextureInfo
    {
        public string Type { get; set; }   // e.g. "Texture2D"
        public string Name { get; set; }   // e.g. "Albedo"
    }

    public class ShaderPreProcessResult
    {
        public string ProcessedSource { get; }
        public List<LineMapping> LineMappings { get; }

        public ShaderMetadata Metadata { get; set; }
        public ShaderPreProcessResult(string processedSource, List<LineMapping> lineMappings, ShaderMetadata metadata)
        {
            ProcessedSource = processedSource;
            LineMappings = lineMappings;
            Metadata = metadata;
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
        Task<ShaderPreProcessResult> PreprocessAsync(string source, string filePath, IReadOnlyList<string> defines = null, IReadOnlyList<string> extensions = null);
    }
}