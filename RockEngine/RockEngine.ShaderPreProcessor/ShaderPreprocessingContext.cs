using System;
using System.Collections.Generic;
using System.Linq;
using RockEngine.ShaderPreprocessor;

namespace RockEngine.ShaderPreProcessor
{
    public class ShaderPreprocessingContext
    {
        public string Source { get; set; }
        public string FilePath { get; set; }
        public IReadOnlyList<string> Defines { get; set; }
        public IReadOnlyList<string> Extensions { get; set; }
        public List<LineMapping> LineMappings { get; }
        public ShaderMetadata Metadata { get; }

        public ShaderPreprocessingContext(string source, string filePath, IReadOnlyList<string> defines,
            IReadOnlyList<string> extensions)
        {
            Source = source;
            FilePath = filePath;
            Defines = defines ?? Array.Empty<string>();
            Extensions = extensions ?? Array.Empty<string>();
            LineMappings = InitializeLineMappings(source, filePath);
            Metadata = new ShaderMetadata();
        }

        private static List<LineMapping> InitializeLineMappings(string source, string filePath)
        {
            var lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            return lines.Select((_, i) => new LineMapping
            {
                OriginalLine = i + 1,
                PreprocessedLine = i + 1,
                SourceFilePath = filePath
            }).ToList();
        }
    }
}