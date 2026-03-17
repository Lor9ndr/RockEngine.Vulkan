using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal static class MaterialCollector
    {
        public static HashSet<string> GetMaterialSampleNames(ITextSnapshot snapshot)
        {
            var names = new HashSet<string>();
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
                        string name = parts[1];
                        string methodName = $"sample{name}";
                        names.Add(methodName);
                    }
                }
            }
            return names;
        }
    }
}