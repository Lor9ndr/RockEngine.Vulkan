using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal class GlslAsyncQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _buffer;
        private bool _disposed;

        // Static lookup for function signatures (loaded once)
        private static readonly Dictionary<string, List<FunctionSignature>> _functionSignatures;

        static GlslAsyncQuickInfoSource()
        {
            _functionSignatures = new Dictionary<string, List<FunctionSignature>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                // Resource name is: [DefaultNamespace].GlslFunctionSignatures.json
                // Replace "RockEngine.ShaderSyntax" with your actual default namespace if different.
                var resourceName = "RockEngine.ShaderSyntax.GlslFunctionSignatures.json";
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var json = reader.ReadToEnd();
                    var signatures = JsonSerializer.Deserialize<List<FunctionSignature>>(json);
                    if (signatures != null)
                    {
                        foreach (var sig in signatures)
                        {
                            if (!_functionSignatures.TryGetValue(sig.Name, out var list))
                            {
                                list = new List<FunctionSignature>();
                                _functionSignatures[sig.Name] = list;
                            }
                            list.Add(sig);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load function signatures: {ex}");
            }
        }

        public GlslAsyncQuickInfoSource(ITextBuffer buffer)
        {
            _buffer = buffer;
        }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return null;
            }

            var triggerPoint = session.GetTriggerPoint(_buffer.CurrentSnapshot);
            if (!triggerPoint.HasValue)
            {
                return null;
            }

            var snapshot = _buffer.CurrentSnapshot;
            var line = snapshot.GetLineFromPosition(triggerPoint.Value.Position);
            var lineText = line.GetText();
            int posInLine = triggerPoint.Value.Position - line.Start.Position;

            // Find the word under the cursor
            int start = posInLine;
            while (start > 0 && (char.IsLetterOrDigit(lineText[start - 1]) || lineText[start - 1] == '_'))
                start--;
            int end = posInLine;
            while (end < lineText.Length && (char.IsLetterOrDigit(lineText[end]) || lineText[end] == '_'))
                end++;

            if (start == end)
                return null;

            string word = lineText.Substring(start, end - start);

            // 1. Check for MATERIAL sample methods
            var materialTextures = ParseMaterialBlocks(snapshot);
            var tex = materialTextures.FirstOrDefault(t => $"sample{t.name}" == word);
            if (tex != default)
            {
                string info = tex.type == "Texture2D"
                    ? $"**{word}**(vec2 uv) → vec4\n\nSamples the {tex.name} texture (generated from [MATERIAL] block)."
                    : $"**{word}**(vec3 uv) → vec4\n\nSamples the {tex.name} texture (generated from [MATERIAL] block).";

                var applicableSpan = snapshot.CreateTrackingSpan(
                    line.Start + start,
                    end - start,
                    SpanTrackingMode.EdgeInclusive
                );

                return new QuickInfoItem(applicableSpan, info);
            }

            // 2. Check for built-in function
            if (_functionSignatures.TryGetValue(word, out var sigs))
            {
                // Build a tooltip with the first overload (or all if you prefer)
                var sig = sigs[0]; // Show first overload
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var content = QuickInfoContentBuilder.BuildForFunction(sig, sigs.Count);

                var applicableSpan = snapshot.CreateTrackingSpan(
                    line.Start + start,
                    end - start,
                    SpanTrackingMode.EdgeInclusive
                );

                return new QuickInfoItem(applicableSpan, content);

            }

            // 3. (Optional) Check for built-in variables – you could add a similar lookup here

            return null;
        }

        private List<(string type, string name)> ParseMaterialBlocks(ITextSnapshot snapshot)
        {
            // Same as before
            var result = new List<(string, string)>();
            string text = snapshot.GetText();
            var materialRegex = new Regex(@"\[MATERIAL\]\s*\{([^}]*)\}", RegexOptions.Singleline);
            var matches = materialRegex.Matches(text);
            foreach (Match match in matches)
            {
                string block = match.Groups[1].Value;
                var lines = block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim().TrimEnd(',', ';');
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        result.Add((parts[0], parts[1]));
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
