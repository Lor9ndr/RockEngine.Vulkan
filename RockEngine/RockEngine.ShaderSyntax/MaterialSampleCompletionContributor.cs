using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(IGlslCompletionContributor))]
    internal class MaterialSampleCompletionContributor : IGlslCompletionContributor
    {
        public IEnumerable<Completion> GetCompletions(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            var textures = ParseMaterialBlocks(snapshot);
            foreach (var tex in textures)
            {
                string methodName = $"sample{tex.name}";
                string description = tex.type == "Texture2D"
                    ? $"{methodName}(vec2 uv) → vec4\nSamples the {tex.name} texture."
                    : $"{methodName}(vec3 uv) → vec4\nSamples the {tex.name} texture.";
                yield return new Completion(methodName, methodName, description, null, null);
            }
        }

        private List<(string type, string name)> ParseMaterialBlocks(ITextSnapshot snapshot)
        {
            var result = new List<(string, string)>();
            string text = snapshot.GetText();
            var materialRegex = new Regex(@"\[MATERIAL\]\s*\{([^}]*)\}", RegexOptions.Singleline);
            var matches = materialRegex.Matches(text);
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
                    }
                }
            }
            return result;
        }
    }
}