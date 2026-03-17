using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal static class FunctionCollector
    {
        public static HashSet<string> GetUserFunctions(ITextSnapshot snapshot)
        {
            var functions = new HashSet<string>();
            string text = snapshot.GetText();

            // Match function definitions: returnType name ( parameters ) { ... }
            // This regex captures the function name.
            // It handles optional qualifiers, whitespace, and line breaks.
            var regex = new Regex(
                @"\b(?:(?:highp|lowp|mediump)\s+)?(\w+)\s+(\w+)\s*\([^)]*\)\s*\{",
                RegexOptions.Compiled | RegexOptions.Multiline);

            var matches = regex.Matches(text);
            foreach (Match match in matches)
            {
                string funcName = match.Groups[2].Value;
                functions.Add(funcName);
            }

            // Also match function prototypes (ending with ;)
            var protoRegex = new Regex(
                @"\b(?:(?:highp|lowp|mediump)\s+)?(\w+)\s+(\w+)\s*\([^)]*\)\s*;",
                RegexOptions.Compiled | RegexOptions.Multiline);

            matches = protoRegex.Matches(text);
            foreach (Match match in matches)
            {
                string funcName = match.Groups[2].Value;
                functions.Add(funcName);
            }

            return functions;
        }
    }
}