using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal class ParameterHelper : IParameter
    {
        public string Name { get; set; }
        public string Documentation { get; set; }
        public Span Locus { get; set; }
        public Span PrettyPrintedLocus { get; set; }
        public ISignature Signature { get; private set; }

        public ParameterHelper(ISignature signature, string name, string type, string doc, Span locus)
        {
            Signature = signature;
            Name = name;
            Documentation = doc;
            Locus = locus;
            PrettyPrintedLocus = locus;
        }
    }
}