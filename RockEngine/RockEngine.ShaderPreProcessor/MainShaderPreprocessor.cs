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

        public async Task<ShaderPreProcessResult> PreprocessAsync(
            string source,
            string filePath,
            IReadOnlyList<string> defines = null,
            IReadOnlyList<string> extensions = null)
        {
            var lineMappings = new List<LineMapping>();

            // Step 1: Initialize 1:1 mapping
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

            // Step 2: Insert required extensions after #version
            if (extensions != null && extensions.Any())
            {
                source = InsertExtensions(source, extensions, lineMappings, filePath);
            }

            // Step 3: Automatically include "Include/common.glsl" if [MATERIAL] block is present
            source = EnsureCommonInclude(source, filePath, lineMappings);

            // Step 4: Process all #include directives (including the newly added one)
            source = await ProcessIncludesAsync(source, Path.GetDirectoryName(filePath), filePath, lineMappings);

            // Step 5: Process [MATERIAL] annotations
            source = ProcessMaterialAnnotations(source, defines ?? Array.Empty<string>(), filePath, lineMappings);

            return new ShaderPreProcessResult(source, lineMappings);
        }

        private string InsertExtensions(string source, IReadOnlyList<string> extensions, List<LineMapping> lineMappings, string filePath)
        {
            // Find the line containing #version
            int versionIndex = source.IndexOf("#version");
            if (versionIndex < 0)
            {
                versionIndex = 0;
            }

            int lineEnd = source.IndexOf('\n', versionIndex);
            if (lineEnd < 0)
            {
                lineEnd = source.Length;
            }

            var extensionLines = new StringBuilder();
            foreach (var ext in extensions)
            {
                extensionLines.AppendLine($"#extension {ext}");
            }

            int insertPos = lineEnd + 1;
            string newSource = source.Insert(insertPos, extensionLines.ToString());

            int versionLineNumber = GetLineNumber(source, versionIndex);
            int addedLineCount = extensions.Count();
            ShiftMappingsAfter(versionLineNumber, addedLineCount, lineMappings);

            int originalLine = GetOriginalLineFromPreprocessed(versionLineNumber, lineMappings);
            for (int i = 0; i < addedLineCount; i++)
            {
                lineMappings.Add(new LineMapping
                {
                    OriginalLine = originalLine,
                    PreprocessedLine = versionLineNumber + i + 1,
                    SourceFilePath = filePath
                });
            }

            return newSource;
        }

        /// <summary>
        /// Automatically inserts #include "Include/common.glsl" if a [MATERIAL] block exists
        /// and the include is not already present.
        /// </summary>
        private string EnsureCommonInclude(string source, string filePath, List<LineMapping> lineMappings)
        {
            // Check if [MATERIAL] block exists
            if (!Regex.IsMatch(source, @"\[MATERIAL\]\s*\{", RegexOptions.Singleline))
            {
                return source;
            }

            // Check if common.glsl is already included
            if (Regex.IsMatch(source, @"#include\s+[""']Include/common\.glsl[""']"))
            {
                return source;
            }

            // Find insertion point: after #version line (or beginning if no #version)
            int insertIndex = 0;
            int lineNumberAfterVersion = 1;
            int versionIndex = source.IndexOf("#version");
            if (versionIndex >= 0)
            {
                int lineEnd = source.IndexOf('\n', versionIndex);
                if (lineEnd < 0)
                {
                    lineEnd = source.Length;
                }

                insertIndex = lineEnd + 1;
                lineNumberAfterVersion = GetLineNumber(source, versionIndex) + 1;
            }
            else
            {
                insertIndex = 0;
                lineNumberAfterVersion = 1;
            }

            string includeLine = "#include \"Include/common.glsl\"\n";
            source = source.Insert(insertIndex, includeLine);

            // Shift existing mappings that are at or after the insertion line
            ShiftMappingsAfter(lineNumberAfterVersion - 1, 1, lineMappings);

            // Add mapping for the new include line
            int originalLine = GetOriginalLineFromPreprocessed(lineNumberAfterVersion, lineMappings);
            lineMappings.Add(new LineMapping
            {
                OriginalLine = originalLine,
                PreprocessedLine = lineNumberAfterVersion,
                SourceFilePath = filePath
            });

            return source;
        }

        private async Task<string> ProcessIncludesAsync(string source, string baseDirectory, string filePath, List<LineMapping> lineMappings)
        {
            var includePattern = @"#include\s+[""'](.+?)[""']";
            var matches = Regex.Matches(source, includePattern);

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                string includeFile = match.Groups[1].Value;
                string includePath = ResolveIncludePath(includeFile, baseDirectory);

                if (!_includeCache.TryGetValue(includePath, out string includeSource))
                {
                    includeSource = await File.ReadAllTextAsync(includePath);
                    includeSource = await ProcessIncludesAsync(includeSource, Path.GetDirectoryName(includePath), filePath, lineMappings);
                    _includeCache[includePath] = includeSource;
                }

                int lineNumberOfInclude = GetLineNumber(source, match.Index);
                int includeLineCount = includeSource.Count(c => c == '\n') + 1;

                ShiftMappingsAfter(lineNumberOfInclude, includeLineCount, lineMappings);

                for (int j = 0; j < includeLineCount; j++)
                {
                    lineMappings.Add(new LineMapping
                    {
                        OriginalLine = GetOriginalLineFromPreprocessed(lineNumberOfInclude, lineMappings),
                        PreprocessedLine = lineNumberOfInclude + j + 1,
                        SourceFilePath = filePath
                    });
                }

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
            {
                return source;
            }

            bool bindlessEnabled = defines?.Contains("BINDLESS_SUPPORTED") ?? false;

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                var blockContent = match.Groups[1].Value;
                var textures = ParseTextureDeclarations(blockContent);
                var generatedCode = GenerateMaterialCode(textures, bindlessEnabled);

                int lineNumberOfBlock = GetLineNumber(source, match.Index);
                int blockLineCount = match.Value.Count(c => c == '\n') + 1;
                int generatedLineCount = generatedCode.Count(c => c == '\n') + 1;
                int delta = generatedLineCount - blockLineCount;

                ShiftMappingsAfter(lineNumberOfBlock + blockLineCount - 1, delta, lineMappings);

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

                source = source.Remove(match.Index, match.Length);
                source = source.Insert(match.Index, generatedCode);
            }

            return source;
        }

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

        private int GetOriginalLineFromPreprocessed(int preprocessedLine, List<LineMapping> lineMappings)
        {
            for (int i = lineMappings.Count - 1; i >= 0; i--)
            {
                if (lineMappings[i].PreprocessedLine == preprocessedLine)
                {
                    return lineMappings[i].OriginalLine;
                }
            }
            return preprocessedLine;
        }

        private int GetLineNumber(string source, int index)
        {
            int line = 1;
            for (int i = 0; i < index; i++)
            {
                if (source[i] == '\n')
                {
                    line++;
                }
            }
            return line;
        }

        private string ResolveIncludePath(string includeFile, string baseDirectory)
        {
            var relativePath = Path.Combine(baseDirectory, includeFile);
            if (File.Exists(relativePath))
            {
                return relativePath;
            }

            if (File.Exists(includeFile))
            {
                return includeFile;
            }

            throw new FileNotFoundException($"Include file not found: {includeFile}");
        }

        private List<(string type, string name)> ParseTextureDeclarations(string block)
        {
            var list = new List<(string, string)>();
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim().TrimEnd(',', ';');
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

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
                    {
                        sb.AppendLine($"vec4 sample{name}(vec2 uv) {{ return texture(uBindlessTextures[nonuniformEXT(material.{name}Index)], uv); }}");
                    }
                    else if (type.StartsWith("Texture3D"))
                    {
                        sb.AppendLine($"vec4 sample{name}(vec3 uv) {{ return texture(uBindlessTextures[nonuniformEXT(material.{name}Index)], uv); }}");
                    }
                }
                sb.AppendLine("#else");
            }

            sb.AppendLine("// Legacy per-texture bindings");
            for (int i = 0; i < textures.Count; i++)
            {
                string layoutLine;
                if (textures[i].type.StartsWith("Texture2D"))
                {
                    layoutLine = $"layout(set = MATERIAL_SET, binding = {i}) uniform sampler2D {textures[i].name};";
                }
                else if (textures[i].type.StartsWith("Texture3D"))
                {
                    layoutLine = $"layout(set = MATERIAL_SET, binding = {i}) uniform sampler3D {textures[i].name};";
                }
                else
                {
                    continue;
                }
                sb.AppendLine(layoutLine);
            }
            sb.AppendLine();

            foreach (var (type, name) in textures)
            {
                if (type.StartsWith("Texture2D"))
                {
                    sb.AppendLine($"vec4 sample{name}(vec2 uv) {{ return texture({name}, uv); }}");
                }
                else if (type.StartsWith("Texture3D"))
                {
                    sb.AppendLine($"vec4 sample{name}(vec3 uv) {{ return texture({name}, uv); }}");
                }
            }

            if (bindlessEnabled)
            {
                sb.AppendLine("#endif // BINDLESS_SUPPORTED");
            }

            return sb.ToString();
        }
    }
}