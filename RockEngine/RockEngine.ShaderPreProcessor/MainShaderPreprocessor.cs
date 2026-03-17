using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RockEngine.ShaderPreprocessor
{
    public class MainShaderPreprocessor : IShaderPreprocessor
    {
        private readonly ConcurrentDictionary<string, string> _includeCache = new ConcurrentDictionary<string, string>();


        public MainShaderPreprocessor()
        {
        }

        public async Task<ShaderPreProcessResult> PreprocessAsync(string source, string filePath, IReadOnlyList<string> defines = null)
        {
            var lineMappings = new List<LineMapping>();

            // Step 1: Initialize 1:1 mapping for every line in the original source
            string[] originalLines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < originalLines.Length; i++)
            {
                lineMappings.Add(new LineMapping
                {
                    OriginalLine = i + 1,
                    PreprocessedLine = i + 1,
                    SourceFilePath = filePath
                });
            }

            // Step 2: Process includes (recursive)
            source = await ProcessIncludesAsync(source, Path.GetDirectoryName(filePath), filePath, lineMappings);

            // Step 3: Process material annotations
            source = ProcessMaterialAnnotations(source, defines ?? Array.Empty<string>(), filePath, lineMappings);

            return new ShaderPreProcessResult(source, lineMappings);
        }

        private async Task<string> ProcessIncludesAsync(string source, string baseDirectory, string filePath, List<LineMapping> lineMappings)
        {
            var includePattern = @"#include\s+[""'](.+?)[""']";
            var matches = Regex.Matches(source, includePattern);

            // Process in reverse to keep indices valid
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                string includeFile = match.Groups[1].Value;
                string includePath = ResolveIncludePath(includeFile, baseDirectory);

                // Read and recursively process the included file
                if (!_includeCache.TryGetValue(includePath, out string includeSource))
                {
                    includeSource = await File.ReadAllTextAsync(includePath);
                    // Recursive call – note: we do NOT track mappings for the included file separately.
                    // We will map all lines from the include back to the original #include line.
                    includeSource = await ProcessIncludesAsync(includeSource, Path.GetDirectoryName(includePath), filePath, lineMappings);
                    _includeCache[includePath] = includeSource;
                }

                int lineNumberOfInclude = GetLineNumber(source, match.Index); // line in current expanded source
                int includeLineCount = includeSource.Count(c => c == '\n') + 1;

                // Shift mappings for all lines after the include directive
                ShiftMappingsAfter(lineNumberOfInclude, includeLineCount, lineMappings);

                // Map each line of the included content to the include directive's original line
                for (int j = 0; j < includeLineCount; j++)
                {
                    lineMappings.Add(new LineMapping
                    {
                        OriginalLine = GetOriginalLineFromPreprocessed(lineNumberOfInclude, lineMappings), // line of #include in original
                        PreprocessedLine = lineNumberOfInclude + j + 1, // new preprocessed lines
                        SourceFilePath = filePath // still the main file (or we could store includePath)
                    });
                }

                // Insert the include source (without #line directives)
                source = source.Remove(match.Index, match.Length);
                source = source.Insert(match.Index, includeSource);
            }

            return source;
        }

        private string ProcessMaterialAnnotations(string source, IReadOnlyList<string> defines, string filePath, List<LineMapping> lineMappings)
        {
            var pattern = @"\[MATERIAL\]\s*\{([^}]*)\}";
            var matches = Regex.Matches(source, pattern, RegexOptions.Singleline);

            if (matches.Count == 0)
                return source;

            bool bindlessEnabled = defines?.Contains("BINDLESS_SUPPORTED") ?? false;

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                var blockContent = match.Groups[1].Value;
                var textures = ParseTextureDeclarations(blockContent);
                var generatedCode = GenerateMaterialCode(textures, bindlessEnabled);

                int lineNumberOfBlock = GetLineNumber(source, match.Index); // line in current expanded source
                int blockLineCount = match.Value.Count(c => c == '\n') + 1;
                int generatedLineCount = generatedCode.Count(c => c == '\n') + 1;
                int delta = generatedLineCount - blockLineCount;

                // Shift mappings after the block
                ShiftMappingsAfter(lineNumberOfBlock + blockLineCount - 1, delta, lineMappings);

                // Map each generated line to the original line of the [MATERIAL] block
                int originalLineOfBlock = GetOriginalLineFromPreprocessed(lineNumberOfBlock, lineMappings);
                for (int j = 0; j < generatedLineCount; j++)
                {
                    lineMappings.Add(new LineMapping
                    {
                        OriginalLine = originalLineOfBlock,
                        PreprocessedLine = lineNumberOfBlock + j + 1,
                        SourceFilePath = filePath
                    });
                }

                // Replace the block with generated code
                source = source.Remove(match.Index, match.Length);
                source = source.Insert(match.Index, generatedCode);
            }

            return source;
        }

        // Helper: shift all mappings with PreprocessedLine > afterLine by delta
        private void ShiftMappingsAfter(int afterLine, int delta, List<LineMapping> lineMappings)
        {
            foreach (var mapping in lineMappings)
            {
                if (mapping.PreprocessedLine > afterLine)
                {
                    mapping.PreprocessedLine += delta;
                }
            }
        }

        // Helper: find the original line number that corresponds to a given preprocessed line
        private int GetOriginalLineFromPreprocessed(int preprocessedLine, List<LineMapping> lineMappings)
        {
            // Search from end to get the most recent mapping (since lines may have been inserted)
            for (int i = lineMappings.Count - 1; i >= 0; i--)
            {
                if (lineMappings[i].PreprocessedLine == preprocessedLine)
                    return lineMappings[i].OriginalLine;
            }
            return preprocessedLine; // fallback (should not happen)
        }

        private int GetLineNumber(string source, int index)
        {
            int line = 1;
            for (int i = 0; i < index; i++)
            {
                if (source[i] == '\n')
                    line++;
            }
            return line;
        }

        private string ResolveIncludePath(string includeFile, string baseDirectory)
        {
            // Try relative to the current file
            var relativePath = Path.Combine(baseDirectory, includeFile);
            if (File.Exists(relativePath))
                return relativePath;

            // Try absolute path
            if (File.Exists(includeFile))
                return includeFile;

            throw new FileNotFoundException($"Include file not found: {includeFile}");
        }

        private List<(string type, string name)> ParseTextureDeclarations(string block)
        {
            var list = new List<(string, string)>();
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim().TrimEnd(',', ';');
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    list.Add((parts[0], parts[1]));
                }
            }
            return list;
        }

        private string GenerateMaterialCode(List<(string type, string name)> textures, bool bindlessEnabled)
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
                    if (type.StartsWith("Texture2D"))
                        sb.AppendLine($"vec4 sample{name}(vec2 uv) {{ return texture(uBindlessTextures[nonuniformEXT(material.{name}Index)], uv); }}");
                    else if (type.StartsWith("Texture3D"))
                        sb.AppendLine($"vec4 sample{name}(vec3 uv) {{ return texture(uBindlessTextures[nonuniformEXT(material.{name}Index)], uv); }}");
                }
                sb.AppendLine("#else");
            }

            sb.AppendLine("// Legacy per-texture bindings");
            for (int i = 0; i < textures.Count; i++)
            {
                string layoutLine;
                if (textures[i].type.StartsWith("Texture2D"))
                    layoutLine = $"layout(set = MATERIAL_SET, binding = {i}) uniform sampler2D u{textures[i].name};";
                else if (textures[i].type.StartsWith("Texture3D"))
                    layoutLine = $"layout(set = MATERIAL_SET, binding = {i}) uniform sampler3D u{textures[i].name};";
                else
                    continue;
                sb.AppendLine(layoutLine);
            }
            sb.AppendLine();

            foreach (var (type, name) in textures)
            {
                if (type.StartsWith("Texture2D"))
                    sb.AppendLine($"vec4 sample{name}(vec2 uv) {{ return texture(u{name}, uv); }}");
                else if (type.StartsWith("Texture3D"))
                    sb.AppendLine($"vec4 sample{name}(vec3 uv) {{ return texture(u{name}, uv); }}");
            }

            if (bindlessEnabled)
                sb.AppendLine("#endif // BINDLESS_SUPPORTED");

            return sb.ToString();
        }
    }
}