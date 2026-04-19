using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(IGlslCompletionContributor))]
    internal class VariableCompletionContributor : IGlslCompletionContributor
    {
        // Cache variables to avoid re‑parsing on every keystroke
        private List<VariableInfo>? _cachedVariables;
        private int _lastVersion = -1;

        private List<VariableInfo> GetVariables(ITextSnapshot snapshot)
        {
            if (_lastVersion != snapshot.Version.VersionNumber)
            {
                _cachedVariables = VariableCollector.GetVariables(snapshot);
                _lastVersion = snapshot.Version.VersionNumber;
            }
            return _cachedVariables;
        }

        public IEnumerable<Completion> GetCompletions(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            // Show user variables only in expression context
            if (!IsExpressionContext(snapshot, triggerPoint))
            {
                yield break;
            }

            var variables = GetVariables(snapshot);
            foreach (var var in variables)
            {
                string description = $"{var.Type} {var.Name}";
                yield return new Completion(var.Name, var.Name, description, null, null);
            }
        }

        /// <summary>
        /// Checks if the cursor is in a context where a new variable is being declared
        /// (i.e., after a type name).
        /// </summary>
        public static bool IsDeclarationContext(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            var line = snapshot.GetLineFromPosition(triggerPoint.Position);
            string lineText = line.GetText();
            int posInLine = triggerPoint.Position - line.Start.Position;

            // Find the previous non‑whitespace token
            int prevEnd = posInLine - 1;
            while (prevEnd >= 0 && char.IsWhiteSpace(lineText[prevEnd]))
            {
                prevEnd--;
            }

            if (prevEnd < 0)
            {
                return false;
            }

            int prevStart = prevEnd;
            while (prevStart >= 0 && (char.IsLetterOrDigit(lineText[prevStart]) || lineText[prevStart] == '_'))
            {
                prevStart--;
            }

            string previousWord = lineText.Substring(prevStart + 1, prevEnd - prevStart);
            return GlslBuiltIns.BasicTypes.Contains(previousWord);
        }

        /// <summary>
        /// Checks if the cursor is in a context where a variable reference can be used
        /// (e.g., after an operator, comma, parenthesis, or at the start of a line).
        /// </summary>
        public static bool IsExpressionContext(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            var line = snapshot.GetLineFromPosition(triggerPoint.Position);
            string lineText = line.GetText();
            int posInLine = triggerPoint.Position - line.Start.Position;

            // Find the first non‑whitespace character before the caret
            int idx = posInLine - 1;
            while (idx >= 0 && char.IsWhiteSpace(lineText[idx]))
            {
                idx--;
            }

            if (idx < 0)
            {
                return true; // start of line – expression context
            }

            char c = lineText[idx];
            // If the character is a letter/digit, we need to see the whole previous token
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                int end = idx;
                int start = idx;
                while (start >= 0 && (char.IsLetterOrDigit(lineText[start]) || lineText[start] == '_'))
                {
                    start--;
                }

                string previousWord = lineText.Substring(start + 1, end - start);

                // If it's a type, it's declaration context, not expression
                if (GlslBuiltIns.BasicTypes.Contains(previousWord))
                {
                    return false;
                }

                // Otherwise, it's expression (e.g., after a variable name)
                return true;
            }

            // Dot indicates member access – we don't want variable completion there
            if (c == '.')
            {
                return false;
            }

            // Any other punctuation (including '(', ',', '=', '+', '-', etc.) indicates expression context
            return true;
        }
    }
}