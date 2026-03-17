using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace RockEngine.ShaderSyntax
{
    internal class GlslClassifier : IClassifier
    {
        private readonly IClassificationType _keywordType;
        private readonly IClassificationType _typeType;
        private readonly IClassificationType _preprocessorType;
        private readonly IClassificationType _materialAnnotationType;
        private readonly IClassificationType _builtInVariableType;
        private readonly IClassificationType _builtInFunctionType;
        private readonly IClassificationType _userFunctionType; // new

        // Built‑in sets (from scraped data)
        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "if", "else", "for", "while", "do", "switch", "case", "default",
            "break", "continue", "return", "discard", "in", "out", "inout",
            "uniform", "layout", "attribute", "varying", "const", "flat",
            "smooth", "noperspective", "centroid", "invariant", "precise"
        };

        private static readonly HashSet<string> Types = new HashSet<string>(GlslBuiltIns.BasicTypes);
        private static readonly HashSet<string> BuiltInVariables = new HashSet<string>(GlslBuiltIns.BuiltInVariables);
        private static readonly HashSet<string> BuiltInFunctions = new HashSet<string>(GlslBuiltIns.BuiltInFunctionNames);

        // Cached user function names per snapshot
        private HashSet<string> _userFunctions;
        private int _currentSnapshotVersion = -1;

        internal GlslClassifier(IClassificationTypeRegistryService registry)
        {
            _keywordType = registry.GetClassificationType(GlslClassificationTypes.GlslKeyword);
            _typeType = registry.GetClassificationType(GlslClassificationTypes.GlslType);
            _preprocessorType = registry.GetClassificationType(GlslClassificationTypes.GlslPreprocessor);
            _materialAnnotationType = registry.GetClassificationType(GlslClassificationTypes.GlslMaterialAnnotation);
            _builtInVariableType = registry.GetClassificationType(GlslClassificationTypes.GlslBuiltInVariable);
            _builtInFunctionType = registry.GetClassificationType(GlslClassificationTypes.GlslBuiltInFunction);
            _userFunctionType = registry.GetClassificationType(GlslClassificationTypes.GlslUserFunction);
        }

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged
        {
            add { }
            remove { }
        }

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            // Update user functions if snapshot changed
            EnsureUserFunctions(span.Snapshot);

            var classifications = new List<ClassificationSpan>();
            string text = span.GetText();

            int lineStart = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n' || i == text.Length - 1)
                {
                    int lineEnd = (i == text.Length - 1) ? i + 1 : i;
                    string line = text.Substring(lineStart, lineEnd - lineStart);
                    ClassifyLine(line, span.Snapshot, span.Start + lineStart, classifications);
                    lineStart = i + 1;
                }
            }

            return classifications;
        }

        private void EnsureUserFunctions(ITextSnapshot snapshot)
        {
            if (_currentSnapshotVersion != snapshot.Version.VersionNumber)
            {
                var userFuncs = FunctionCollector.GetUserFunctions(snapshot);
                var materialSampleNames = MaterialCollector.GetMaterialSampleNames(snapshot);
                _userFunctions = new HashSet<string>(userFuncs);
                _userFunctions.UnionWith(materialSampleNames);
                _currentSnapshotVersion = snapshot.Version.VersionNumber;
            }
        }

        private void ClassifyLine(string line, ITextSnapshot snapshot, int lineStartPos, List<ClassificationSpan> classifications)
        {
            int pos = 0;
            int len = line.Length;

            while (pos < len)
            {
                while (pos < len && char.IsWhiteSpace(line[pos])) pos++;
                if (pos >= len) break;

                if (line[pos] == '#')
                {
                    int start = pos;
                    while (pos < len && !char.IsWhiteSpace(line[pos]) && line[pos] != '\r' && line[pos] != '\n')
                        pos++;
                    var span = new SnapshotSpan(snapshot, lineStartPos + start, pos - start);
                    classifications.Add(new ClassificationSpan(span, _preprocessorType));
                    continue;
                }

                if (pos + 10 <= len && line.Substring(pos, 10) == "[MATERIAL]")
                {
                    var span = new SnapshotSpan(snapshot, lineStartPos + pos, 10);
                    classifications.Add(new ClassificationSpan(span, _materialAnnotationType));
                    pos += 10;
                    continue;
                }

                if (char.IsLetter(line[pos]) || line[pos] == '_')
                {
                    int start = pos;
                    while (pos < len && (char.IsLetterOrDigit(line[pos]) || line[pos] == '_'))
                        pos++;
                    string word = line.Substring(start, pos - start);

                    IClassificationType type = GetClassificationForWord(word);
                    if (type != null)
                    {
                        var span = new SnapshotSpan(snapshot, lineStartPos + start, pos - start);
                        classifications.Add(new ClassificationSpan(span, type));
                    }
                }
                else
                {
                    pos++;
                }
            }
        }

        private IClassificationType GetClassificationForWord(string word)
        {
            if (Keywords.Contains(word))
                return _keywordType;

            if (Types.Contains(word))
                return _typeType;

            if (BuiltInVariables.Contains(word))
                return _builtInVariableType;

            if (BuiltInFunctions.Contains(word))
                return _builtInFunctionType;

            // Check user-defined functions
            if (_userFunctions != null && _userFunctions.Contains(word))
                return _userFunctionType;

            return null;
        }
    }
}
