using System.IO;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal static class ShaderStageHelper
    {
        public static ShaderStage GetStage(ITextBuffer buffer)
        {
            if (buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc))
            {
                string ext = Path.GetExtension(doc.FilePath).ToLowerInvariant();
                return ext switch
                {
                    ".vert" => ShaderStage.Vertex,
                    ".frag" => ShaderStage.Fragment,
                    ".comp" => ShaderStage.Compute,
                    ".geom" => ShaderStage.Geometry,
                    ".tesc" => ShaderStage.TessellationControl,
                    ".tese" => ShaderStage.TessellationEvaluation,
                    _ => ShaderStage.Unknown
                };
            }
            return ShaderStage.Unknown;
        }
    }
}