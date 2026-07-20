using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RockEngine.ShaderPreprocessor;   // for LineMapping, ShaderPreprocessingContext etc.

namespace RockEngine.ShaderPreProcessor.Steps
{
    public class ExtensionInserterStep : IShaderPreprocessorStep
    {
        public Task ExecuteAsync(ShaderPreprocessingContext context)
        {
            if (context.Extensions == null || !context.Extensions.Any())
            {
                return Task.CompletedTask;
            }

            // Find the #version line
            int versionIndex = context.Source.IndexOf("#version");
            if (versionIndex < 0)
            {
                versionIndex = 0;
            }

            int lineEnd = context.Source.IndexOf('\n', versionIndex);
            if (lineEnd < 0)
            {
                lineEnd = context.Source.Length;
            }

            // Build the extension lines
            var extensionLines = new StringBuilder();
            foreach (var ext in context.Extensions)
            {
                extensionLines.AppendLine($"#extension {ext}");
            }

            int insertPos = lineEnd + 1;
            string newSource = context.Source.Insert(insertPos, extensionLines.ToString());

            // Update line mappings
            int versionLineNumber = GetLineNumber(context.Source, versionIndex);
            int addedLineCount = context.Extensions.Count;

            ShiftMappingsAfter(versionLineNumber, addedLineCount, context.LineMappings);

            int originalLine = GetOriginalLineFromPreprocessed(versionLineNumber, context.LineMappings);
            for (int i = 0; i < addedLineCount; i++)
            {
                context.LineMappings.Add(new LineMapping
                {
                    OriginalLine = originalLine,
                    PreprocessedLine = versionLineNumber + i + 1,
                    SourceFilePath = context.FilePath
                });
            }

            context.Source = newSource;
            return Task.CompletedTask;
        }

        // --- Helpers (reused from the original preprocessor) ---
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

        private void ShiftMappingsAfter(int afterLine, int delta, List<LineMapping> mappings)
        {
            foreach (var m in mappings)
            {
                if (m.PreprocessedLine > afterLine)
                {
                    m.PreprocessedLine += delta;
                }
            }
        }

        private int GetOriginalLineFromPreprocessed(int preprocessedLine, List<LineMapping> mappings)
        {
            for (int i = mappings.Count - 1; i >= 0; i--)
            {
                if (mappings[i].PreprocessedLine == preprocessedLine)
                {
                    return mappings[i].OriginalLine;
                }
            }

            return preprocessedLine;
        }
    }
}