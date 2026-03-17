using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(IGlslCompletionContributor))]
    internal class BuiltInCompletionContributor : IGlslCompletionContributor
    {
        private static readonly string[] Keywords =
        {
            "if", "else", "for", "while", "do", "switch", "case", "default",
            "break", "continue", "return", "discard", "in", "out", "inout",
            "uniform", "layout", "attribute", "varying", "const", "flat",
            "smooth", "noperspective", "centroid", "invariant", "precise"
        };

        private static readonly string[] Types = GlslBuiltIns.BasicTypes;
        private static readonly HashSet<string> TypesSet = new HashSet<string>(Types);

        public IEnumerable<Completion> GetCompletions(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            bool inVariableContext = IsVariableContext(snapshot, triggerPoint);
            foreach (var kw in Keywords)
            {
                yield return new Completion(kw, kw, "GLSL keyword", null, null);
            }

            if (!inVariableContext)
            {
                foreach (var type in Types)
                {
                    yield return new Completion(type, type, "GLSL type", null, null);
                }
            }
        }

        private bool IsVariableContext(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            var line = snapshot.GetLineFromPosition(triggerPoint.Position);
            string lineText = line.GetText();
            int posInLine = triggerPoint.Position - line.Start.Position;

            int start = posInLine - 1;
            while (start >= 0 && char.IsWhiteSpace(lineText[start]))
            {
                start--;
            }

            int end = start + 1;
            while (start >= 0 && (char.IsLetterOrDigit(lineText[start]) || lineText[start] == '_'))
            {
                start--;
            }

            string previousWord = start < end - 1 ? lineText.Substring(start + 1, (end - start - 1)) : "";

            return TypesSet.Contains(previousWord);
        }
    }
}
