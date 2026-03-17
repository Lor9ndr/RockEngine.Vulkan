using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal class GlslSignatureHelpSource : ISignatureHelpSource
    {
        private readonly ITextBuffer _buffer;
        private readonly IEnumerable<ISignatureHelpContributor> _contributors;
        private bool _disposed;

        public GlslSignatureHelpSource(ITextBuffer buffer, IEnumerable<ISignatureHelpContributor> contributors)
        {
            _buffer = buffer;
            _contributors = contributors;
        }

        public void AugmentSignatureHelpSession(ISignatureHelpSession session, IList<ISignature> signatures)
        {
            Debug.WriteLine("AugmentSignatureHelpSession entered");
            if (_disposed) return;

            var triggerPoint = session.GetTriggerPoint(_buffer.CurrentSnapshot);
            if (!triggerPoint.HasValue)
            {
                Debug.WriteLine("No trigger point");
                return;
            }

            var snapshot = _buffer.CurrentSnapshot;
            var line = snapshot.GetLineFromPosition(triggerPoint.Value.Position);
            var lineText = line.GetText();

            if (lineText.Length == 0)
            {
                Debug.WriteLine("Line empty");
                return;
            }

            int posInLine = triggerPoint.Value.Position - line.Start.Position;
            if (posInLine < 0 || posInLine > lineText.Length)
            {
                Debug.WriteLine($"posInLine out of range: {posInLine}");
                return;
            }

            int searchStart = Math.Min(posInLine, lineText.Length - 1);
            int parenPos = lineText.LastIndexOf('(', searchStart);
            if (parenPos < 0)
            {
                Debug.WriteLine("No '(' found");
                return;
            }

            int start = parenPos - 1;
            while (start >= 0 && char.IsWhiteSpace(lineText[start]))
                start--;

            int end = start + 1;
            while (start >= 0 && (char.IsLetterOrDigit(lineText[start]) || lineText[start] == '_'))
                start--;

            int identifierStart = start + 1;
            if (identifierStart >= end)
            {
                Debug.WriteLine("No identifier before '('");
                return;
            }

            string functionName = lineText.Substring(identifierStart, end - identifierStart).Trim();
            Debug.WriteLine($"Found function name: '{functionName}'");

            var materialTextures = ParseMaterialBlocks(snapshot);
            Debug.WriteLine($"Found {materialTextures.Count} textures in MATERIAL blocks");

            foreach (var contributor in _contributors)
            {
                try
                {
                    var sigs = contributor.GetSignatures(functionName, snapshot, triggerPoint.Value);
                    foreach (var sig in sigs)
                        signatures.Add(sig);
                }
                catch (Exception ex)
                {
                    Debug.Fail($"Error in signature help contributor {contributor.GetType().Name}: {ex}");
                }
            }
        }

        public ISignature GetBestMatch(ISignatureHelpSession session)
        {
            Debug.WriteLine("GetBestMatch called");
            if (session.Signatures.Any())
                return session.Signatures.First();
            return null;
        }

        private List<(string type, string name)> ParseMaterialBlocks(ITextSnapshot snapshot)
        {
            var result = new List<(string, string)>();
            string text = snapshot.GetText();
            var materialRegex = new Regex(@"\[MATERIAL\]\s*\{([^}]*)\}", RegexOptions.Singleline);
            var matches = materialRegex.Matches(text);
            Debug.WriteLine($"ParseMaterialBlocks: found {matches.Count} [MATERIAL] blocks");
            foreach (Match match in matches)
            {
                string block = match.Groups[1].Value;
                var lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim().TrimEnd(',', ';');
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        result.Add((parts[0], parts[1]));
                        Debug.WriteLine($"  Parsed texture: {parts[0]} {parts[1]}");
                    }
                }
            }
            return result;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}