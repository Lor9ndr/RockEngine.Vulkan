using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(ISignatureHelpContributor))]
    internal class BuiltInSignatureHelpContributor : ISignatureHelpContributor
    {
        private static readonly List<FunctionSignature> _signatures;

        static BuiltInSignatureHelpContributor()
        {
            // Load embedded JSON resource
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("RockEngine.ShaderSyntax.GlslFunctionSignatures.json");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            _signatures = JsonSerializer.Deserialize<List<FunctionSignature>>(json);
        }

        public IEnumerable<ISignature> GetSignatures(string functionName, ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            var matches = _signatures.Where(s => s.Name == functionName).ToList();
            foreach (var match in matches)
            {
                // Build parameter string
                var paramString = string.Join(", ", match.Parameters.Select(p => p.Type + (string.IsNullOrEmpty(p.Name) ? "" : " " + p.Name)));
                var applicableSpan = snapshot.CreateTrackingSpan(new Span(triggerPoint.Position, 0), SpanTrackingMode.EdgeInclusive);
                var signature = new SignatureHelper(match.Name, paramString, match.Description, applicableSpan);
                yield return signature;
            }
        }
    }
}