namespace RockEngine.ShaderPreProcessor
{
    public class AnnotationSymbol
    {
        public string Name { get; set; }
        public SymbolKind Kind { get; set; }   // Function, Variable, etc.
        public string Description { get; set; }
        public string Detail { get; set; }     // e.g. "Texture2D"
    }
}