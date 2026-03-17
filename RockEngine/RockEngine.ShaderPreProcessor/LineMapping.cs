namespace RockEngine.ShaderPreprocessor
{
    public class LineMapping
    {
        public int OriginalLine { get; set; }   // 1‑based original line
        public int PreprocessedLine { get; set; } // 1‑based line in final preprocessed source
        public string SourceFilePath { get; set; }
    }
}