using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal class VariableInfo
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public SnapshotSpan Span { get; set; } // for potential navigation
    }
}