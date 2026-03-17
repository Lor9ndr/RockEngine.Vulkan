using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(IGlslCompletionContributor))]
    internal class VariableCompletionContributor : IGlslCompletionContributor
    {

        public IEnumerable<Completion> GetCompletions(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            var variables = VariableCollector.GetVariables(snapshot);

            if (!IsVariableContext(snapshot, triggerPoint))
                yield break;

            foreach (var var in variables)
            {
                string description = $"{var.Type} {var.Name}";
                yield return new Completion(var.Name, var.Name, description, null, null);
            }
        }

        public static bool IsVariableContext(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            var line = snapshot.GetLineFromPosition(triggerPoint.Position);
            string lineText = line.GetText();
            int caretPos = triggerPoint.Position - line.Start.Position;

            // 1. Find the start of the current word (if any) at the caret
            int startCurrent = caretPos;
            while (startCurrent > 0 && (char.IsLetterOrDigit(lineText[startCurrent - 1]) || lineText[startCurrent - 1] == '_'))
                startCurrent--;

            // 2. Find the end of the previous token (skip whitespace backwards)
            int prevEnd = startCurrent - 1;
            while (prevEnd >= 0 && char.IsWhiteSpace(lineText[prevEnd]))
                prevEnd--;

            if (prevEnd < 0)
                return false;

            // 3. Find the start of that previous token
            int prevStart = prevEnd;
            while (prevStart >= 0 && (char.IsLetterOrDigit(lineText[prevStart]) || lineText[prevStart] == '_'))
                prevStart--;

            // 4. Extract the previous token
            string previousWord = lineText.Substring(prevStart + 1, prevEnd - prevStart);

            // 5. Check if it's a GLSL type
            return GlslBuiltIns.BasicTypes.Contains(previousWord);
        }
    }
}