using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RockEngine.ShaderPreprocessor;

namespace RockEngine.ShaderPreProcessor.Steps
{
    public class AnnotationProcessorStep : IShaderPreprocessorStep
    {
        private readonly IEnumerable<IAnnotationHandler> _handlers;

        public AnnotationProcessorStep(IEnumerable<IAnnotationHandler> handlers)
        {
            _handlers = handlers;
        }

        public async Task ExecuteAsync(ShaderPreprocessingContext context)
        {
            // Process blocks in reverse order to avoid index shifts
            // First, collect all blocks with their handlers
            var allBlocks = new List<(IAnnotationHandler handler, Match match)>();

            foreach (var handler in _handlers)
            {
                var pattern = $@"\[{Regex.Escape(handler.AnnotationName)}\]\s*\{{([^}}]*)\}}";
                var matches = Regex.Matches(context.Source, pattern, RegexOptions.Singleline);
                foreach (Match match in matches)
                {
                    allBlocks.Add((handler, match));
                }
            }

            // Sort by start index descending to process from end to start
            allBlocks = allBlocks.OrderByDescending(x => x.match.Index).ToList();

            foreach (var (handler, match) in allBlocks)
            {
                var blockData = new AnnotationBlockData
                {
                    AnnotationName = handler.AnnotationName,
                    BlockContent = match.Groups[1].Value,
                    RegexMatch = match,
                    StartIndex = match.Index,
                    EndIndex = match.Index + match.Length
                };

                var replacement = await handler.ProcessBlockAsync(blockData, context);

                // Calculate line numbers for mapping
                int lineNumberOfBlock = GetLineNumber(context.Source, match.Index);
                int blockLineCount = match.Value.Count(c => c == '\n') + 1;
                int newLineCount = replacement.NewCode.Count(c => c == '\n') + 1;
                int delta = newLineCount - blockLineCount;

                // Shift line mappings for all lines after the block
                ShiftMappingsAfter(lineNumberOfBlock + blockLineCount - 1, delta, context.LineMappings);

                // Insert new mappings for the generated lines, pointing to the original block location
                int originalLineOfBlock = GetOriginalLineFromPreprocessed(lineNumberOfBlock, context.LineMappings);
                for (int j = 0; j < newLineCount; j++)
                {
                    context.LineMappings.Add(new LineMapping
                    {
                        OriginalLine = originalLineOfBlock,
                        PreprocessedLine = lineNumberOfBlock + j + 1,
                        SourceFilePath = context.FilePath
                    });
                }

                // Replace source text
                context.Source = context.Source.Remove(match.Index, match.Length)
                                             .Insert(match.Index, replacement.NewCode);

                // Apply any metadata mutation
                replacement.MetadataMutator?.Invoke(context.Metadata);
            }
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
    }
}
