using System.Collections.Generic;
using System.Threading.Tasks;

namespace RockEngine.ShaderPreProcessor
{
    public interface IAnnotationHandler
    {
        /// <summary>The unique name of the annotation (e.g. "MATERIAL").</summary>
        string AnnotationName { get; }

        IEnumerable<AnnotationSymbol> GetSymbols(string blockContent);

        /// <summary>
        /// Given the content of a single annotation block (the text between { and }),
        /// return the replacement GLSL code and, optionally, modify the shader context.
        /// </summary>
        Task<AnnotationReplacement> ProcessBlockAsync(AnnotationBlockData block, ShaderPreprocessingContext context);
    }
}