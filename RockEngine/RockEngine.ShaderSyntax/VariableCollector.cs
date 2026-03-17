using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal static class VariableCollector
    {
        private static readonly HashSet<string> GlslTypes = new HashSet<string>
        {
            "void", "bool", "int", "uint", "float", "double",
            "vec2", "vec3", "vec4", "bvec2", "bvec3", "bvec4",
            "ivec2", "ivec3", "ivec4", "uvec2", "uvec3", "uvec4",
            "mat2", "mat3", "mat4", "mat2x2", "mat2x3", "mat2x4",
            "mat3x2", "mat3x3", "mat3x4", "mat4x2", "mat4x3", "mat4x4",
            "sampler1D", "sampler2D", "sampler3D", "samplerCube",
            "sampler1DShadow", "sampler2DShadow", "samplerCubeShadow",
            "sampler1DArray", "sampler2DArray", "samplerBuffer",
            "sampler2DMS", "sampler2DMSArray",
            "Texture2D", "Texture3D", "TextureCube", "Texture2DArray"
        };

        public static List<VariableInfo> GetVariables(ITextSnapshot snapshot)
        {
            var variables = new List<VariableInfo>();
            string text = snapshot.GetText();

            var typePattern = string.Join("|", GlslTypes.Select(Regex.Escape));
            var regex = new Regex($@"\b({typePattern})\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*([,;=])", RegexOptions.Compiled);

            var matches = regex.Matches(text);
            foreach (Match match in matches)
            {
                string type = match.Groups[1].Value;
                string name = match.Groups[2].Value;
                char nextChar = match.Groups[3].Value[0];

                int start = match.Groups[2].Index;
                int length = name.Length;
                var span = new SnapshotSpan(snapshot, start, length);
                variables.Add(new VariableInfo { Type = type, Name = name, Span = span });

                int pos = match.Index + match.Length;
                while (nextChar == ',')
                {
                    var remainder = text.Substring(pos);
                    var commaRegex = new Regex(@"^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*([,;=])");
                    var commaMatch = commaRegex.Match(remainder);
                    if (commaMatch.Success)
                    {
                        name = commaMatch.Groups[1].Value;
                        nextChar = commaMatch.Groups[2].Value[0];
                        start = pos + commaMatch.Groups[1].Index;
                        span = new SnapshotSpan(snapshot, start, name.Length);
                        variables.Add(new VariableInfo { Type = type, Name = name, Span = span });
                        pos += commaMatch.Length;
                    }
                    else break;
                }
            }
            return variables;
        }
    }
}

