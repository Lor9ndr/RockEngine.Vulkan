using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RockEngine.ShaderPreProcessor;

namespace RockEngine.ShaderPreprocessor
{
    public class MaterialAnnotationHandler : IAnnotationHandler
    {
        public string AnnotationName => "MATERIAL";

        /// <summary>
        /// Core processing – generate GLSL from [MATERIAL] block
        /// </summary>
        /// <param name="block"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task<AnnotationReplacement> ProcessBlockAsync(
            AnnotationBlockData block, ShaderPreprocessingContext context)
        {
            var textures = ParseTextureDeclarations(block.BlockContent);
            bool bindlessEnabled = context.Defines.Contains("BINDLESS_SUPPORTED");

            string generatedCode = GenerateMaterialCode(textures, bindlessEnabled);

            var replacement = new AnnotationReplacement
            {
                NewCode = generatedCode,
                MetadataMutator = meta =>
                {
                    // Use the new AddTextures helper (see updated ShaderMetadata)
                    meta.AddTextures(textures.Select(t => new TextureInfo
                    {
                        Type = t.type,
                        Name = t.name
                    }));
                }
            };

            return Task.FromResult(replacement);
        }

        /// <summary>
        /// Symbol extraction – used by syntax highlighter / IntelliSense
        /// </summary>
        /// <param name="blockContent"></param>
        /// <returns></returns>
        public IEnumerable<AnnotationSymbol> GetSymbols(string blockContent)
        {
            var textures = ParseTextureDeclarations(blockContent);
            foreach (var (type, name) in textures)
            {
                // The generated sample function
                yield return new AnnotationSymbol
                {
                    Name = $"sample{name}",
                    Kind = SymbolKind.Function,
                    Description = $"Sample the {name} texture",
                    Detail = type   // e.g. "Texture2D"
                };

                // The texture uniform itself (for legacy mode)
                yield return new AnnotationSymbol
                {
                    Name = name,
                    Kind = SymbolKind.Variable,
                    Description = $"{type} texture uniform",
                    Detail = type
                };
            }
        }

        /// <summary>
        ///  Private helpers – identical to original code
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private List<(string type, string name)> ParseTextureDeclarations(string block)
        {
            var list = new List<(string, string)>();
            var lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim().TrimEnd(',', ';');
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                var parts = trimmed.Split(new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    list.Add((parts[0], parts[1]));
                }
            }
            return list;
        }

        private string GenerateMaterialCode(
            List<(string type, string name)> textures, bool bindlessEnabled)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Automatically generated from [MATERIAL] annotation");

            if (bindlessEnabled)
            {
                sb.AppendLine("#ifdef BINDLESS_SUPPORTED");
                sb.AppendLine("layout(set = MATERIAL_SET, binding = 0) uniform sampler2D uBindlessTextures[];");
                sb.AppendLine("layout(push_constant) uniform MaterialIndices");
                sb.AppendLine("{");
                for (int i = 0; i < textures.Count; i++)
                {
                    sb.AppendLine($"    uint {textures[i].name}Index;");
                }

                sb.AppendLine("} material;");
                sb.AppendLine();

                foreach (var (type, name) in textures)
                {
                    string uvParam = type.StartsWith("Texture2D") ? "vec2 uv" : "vec3 uv";
                    sb.AppendLine($"vec4 sample{name}({uvParam}) {{ return texture(uBindlessTextures[nonuniformEXT(material.{name}Index)], uv); }}");
                }

                sb.AppendLine("#else");
            }

            sb.AppendLine("// Legacy per-texture bindings");
            for (int i = 0; i < textures.Count; i++)
            {
                string samplerType = textures[i].type.StartsWith("Texture2D") ? "sampler2D" : "sampler3D";
                sb.AppendLine($"layout(set = MATERIAL_SET, binding = {i}) uniform {samplerType} {textures[i].name};");
            }
            sb.AppendLine();

            foreach (var (type, name) in textures)
            {
                string uvParam = type.StartsWith("Texture2D") ? "vec2 uv" : "vec3 uv";
                sb.AppendLine($"vec4 sample{name}({uvParam}) {{ return texture({name}, uv; }}");
            }

            if (bindlessEnabled)
            {
                sb.AppendLine("#endif // BINDLESS_SUPPORTED");
            }

            return sb.ToString();
        }
    }
}
