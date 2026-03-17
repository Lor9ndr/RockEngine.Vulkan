using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace GlslDocScraper
{
    public class ParameterInfo
    {
        public string Type { get; set; }
        public string Name { get; set; }
    }

    public class FunctionSignature
    {
        public string ReturnType { get; set; }
        public string Name { get; set; }
        public List<ParameterInfo> Parameters { get; set; } = new();
        public string Description { get; set; }
        public string DocumentationUrl { get; set; }
    }

    internal class Program
    {
        static async Task Main(string[] args)
        {
            var baseUrl = "https://docs.vulkan.org/glsl/latest/chapters/";
            var urls = new[]
            {
                "variables.html",
                "builtins.html",
                "builtinfunctions.html"
            };

            var allTypes = new HashSet<string>();
            var allBuiltIns = new HashSet<string>();
            var allFunctionNames = new HashSet<string>();
            var functionSignatures = new List<FunctionSignature>();

            var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader());

            foreach (var url in urls)
            {
                var fullUrl = baseUrl + url;
                var document = await context.OpenAsync(fullUrl);

                if (url.Contains("variables"))
                {
                    ExtractTypes(document, allTypes);
                    ExtractBuiltInVariables(document, allBuiltIns);
                }
                else if (url.Contains("builtins"))
                {
                    ExtractBuiltInVariables(document, allBuiltIns);
                }
                else if (url.Contains("builtinfunctions"))
                {
                    ExtractFunctions(document, fullUrl, allFunctionNames, functionSignatures);
                }
            }

            // Write arrays for completion
            var sb = new StringBuilder();
            sb.AppendLine("// Auto-generated from GLSL documentation");
            sb.AppendLine("// Do not modify manually – regenerate with GlslDocScraper");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace RockEngine.ShaderSyntax");
            sb.AppendLine("{");
            sb.AppendLine(@"
    public class ParameterInfo
    {
        public string Type { get; set; }
        public string Name { get; set; }
    }

    public class FunctionSignature
    {
        public string ReturnType { get; set; }
        public string Name { get; set; }
        public List<ParameterInfo> Parameters { get; set; } = new();
        public string Description { get; set; }
        public string DocumentationUrl { get; set; }
    }
");
            sb.AppendLine("    internal static class GlslBuiltIns");
            sb.AppendLine("    {");
            WriteArray(sb, "BasicTypes", allTypes);
            sb.AppendLine();
            WriteArray(sb, "BuiltInVariables", allBuiltIns);
            sb.AppendLine();
            WriteArray(sb, "BuiltInFunctionNames", allFunctionNames);
            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText("GlslBuiltIns.cs", sb.ToString());

            // Write signatures for signature help
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText("GlslFunctionSignatures.json", JsonSerializer.Serialize(functionSignatures, jsonOptions));

            Console.WriteLine("Generated GlslBuiltIns.cs and GlslFunctionSignatures.json");
        }

        // ---------- Types (unchanged) ----------
        private static void ExtractTypes(IDocument doc, HashSet<string> types)
        {
            var tables = doc.QuerySelectorAll("table");
            foreach (var table in tables)
            {
                var headerRow = table.QuerySelector("thead tr");
                if (headerRow == null) continue;

                var headers = headerRow.QuerySelectorAll("th").Select(th => th.TextContent.Trim()).ToArray();
                int typeCol = Array.FindIndex(headers, h => h.Equals("Type", StringComparison.OrdinalIgnoreCase));
                if (typeCol == -1) continue;

                var rows = table.QuerySelectorAll("tbody tr");
                foreach (var row in rows)
                {
                    var cells = row.QuerySelectorAll("td");
                    if (typeCol >= cells.Length) continue;

                    var typeName = cells[typeCol].TextContent.Trim();
                    if (IsValidIdentifier(typeName))
                        types.Add(typeName);
                }
            }
        }

        // ---------- Improved built-in variable extraction ----------
        private static void ExtractBuiltInVariables(IDocument doc, HashSet<string> builtIns)
        {
            // Find all <pre><code> blocks
            var codeBlocks = doc.QuerySelectorAll("pre code");
            foreach (var block in codeBlocks)
            {
                var lines = block.TextContent.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    // Match declarations like: "in int gl_VertexID;" or "out vec4 gl_Position;"
                    // Also handle arrays: "float gl_ClipDistance[];"
                    var match = Regex.Match(trimmed,
                        @"\b(?:in|out|patch|uniform|buffer|shared)?\s*\w+\s+(\bgl_\w+)\s*(\[.*?\])?\s*[;=]");
                    if (match.Success)
                    {
                        var varName = match.Groups[1].Value;
                        if (IsValidIdentifier(varName))
                            builtIns.Add(varName);
                    }
                }
            }

            // Also look inside <dt> elements (definition terms)
            var terms = doc.QuerySelectorAll("dt");
            foreach (var term in terms)
            {
                var text = term.TextContent.Trim();
                var match = Regex.Match(text, @"\b(gl_\w+)\b");
                if (match.Success)
                {
                    var varName = match.Groups[1].Value;
                    if (IsValidIdentifier(varName))
                        builtIns.Add(varName);
                }
            }
        }

        private static void ExtractFunctions(IDocument doc, string pageUrl, HashSet<string> names, List<FunctionSignature> signatures)
        {
            bool IsHeading(IElement el) =>
                el.TagName.Equals("H2", StringComparison.OrdinalIgnoreCase) ||
                el.TagName.Equals("H3", StringComparison.OrdinalIgnoreCase);

            var headings = doc.QuerySelectorAll("h2, h3");
            foreach (var h in headings)
            {
                var headingText = h.TextContent.Trim();
                if (headingText.Contains(' ') || headingText.Length > 30)
                    continue;

                if (IsValidIdentifier(headingText) && !headingText.StartsWith("gl_"))
                    names.Add(headingText);

                // ----- Get the documentation URL from the anchor inside the heading -----
                string docUrl = null;
                var anchor = h.QuerySelector("a.anchor");
                if (anchor != null)
                {
                    var href = anchor.GetAttribute("href");
                    if (!string.IsNullOrEmpty(href))
                    {
                        // Combine base page URL with the fragment (href starts with '#')
                        docUrl = pageUrl + href;
                    }
                }

                // ----- Locate the <pre> block following this heading -----
                IElement preElement = null;
                var next = h.NextElementSibling;
                while (next != null && !IsHeading(next))
                {
                    preElement = next.QuerySelector("pre");
                    if (preElement != null)
                        break;
                    next = next.NextElementSibling;
                }

                if (preElement == null) continue;

                var codeElement = preElement.QuerySelector("code") ?? preElement;
                var codeText = codeElement.TextContent;
                var lines = codeText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "glsl") continue;

                    var sigMatch = Regex.Match(trimmed,
                        @"^(?:(?:highp|lowp|mediump)\s+)?([\w<>]+)\s+(\w+)\s*\(([^)]*)\)\s*;?$");
                    if (!sigMatch.Success) continue;

                    var returnType = sigMatch.Groups[1].Value;
                    var funcName = sigMatch.Groups[2].Value;
                    var paramsStr = sigMatch.Groups[3].Value;

                    if (IsValidIdentifier(funcName))
                        names.Add(funcName);

                    var parameters = ParseParameters(paramsStr);

                    // ----- Extract description (unchanged) -----
                    string desc = "";
                    var container = preElement.ParentElement.ParentElement;
                    if (container != null)
                    {
                        var sibling = container.NextElementSibling;
                        while (sibling != null && !IsHeading(sibling))
                        {
                            var p = sibling.QuerySelector("p");
                            if (p != null)
                            {
                                desc = p.TextContent.Trim();
                                break;
                            }
                            sibling = sibling.NextElementSibling;
                        }
                    }

                    if (string.IsNullOrEmpty(desc))
                    {
                        var sibling = preElement.NextElementSibling;
                        while (sibling != null && !IsHeading(sibling))
                        {
                            var p = sibling.QuerySelector("p");
                            if (p != null)
                            {
                                desc = p.TextContent.Trim();
                                break;
                            }
                            sibling = sibling.NextElementSibling;
                        }
                    }

                    if (string.IsNullOrEmpty(desc))
                    {
                        var nextNode = preElement.NextSibling;
                        while (nextNode != null && !(nextNode is IElement elem && IsHeading(elem)))
                        {
                            if (nextNode.NodeType == NodeType.Text)
                            {
                                var text = nextNode.TextContent.Trim();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    desc = text;
                                    break;
                                }
                            }
                            nextNode = nextNode.NextSibling;
                        }
                    }

                    signatures.Add(new FunctionSignature
                    {
                        ReturnType = returnType,
                        Name = funcName,
                        Parameters = parameters,
                        Description = desc,
                        DocumentationUrl = docUrl   // <-- assign the URL
                    });
                }
            }
        }

        private static List<ParameterInfo> ParseParameters(string paramsStr)
        {
            var parameters = new List<ParameterInfo>();
            if (string.IsNullOrWhiteSpace(paramsStr))
                return parameters;

            var paramParts = paramsStr.Split(',');
            foreach (var p in paramParts)
            {
                var pTrim = p.Trim();
                if (string.IsNullOrWhiteSpace(pTrim)) continue;

                var pMatch = Regex.Match(pTrim,
                    @"^(?:(?:highp|lowp|mediump|out|inout|in)\s+)*([\w<>]+)(?:\s+(\w+))?$");
                if (pMatch.Success)
                {
                    var type = pMatch.Groups[1].Value;
                    var name = pMatch.Groups[2].Success ? pMatch.Groups[2].Value : "";
                    parameters.Add(new ParameterInfo { Type = type, Name = name });
                }
                else
                {
                    parameters.Add(new ParameterInfo { Type = pTrim, Name = "" });
                }
            }
            return parameters;
        }

        private static bool IsValidIdentifier(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!char.IsLetter(s[0]) && s[0] != '_') return false;
            return s.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        private static void WriteArray(StringBuilder sb, string name, HashSet<string> values)
        {
            sb.AppendLine($"        public static readonly string[] {name} = new[]");
            sb.AppendLine("        {");
            foreach (var v in values.OrderBy(v => v))
                sb.AppendLine($"            \"{v}\",");
            sb.AppendLine("        };");
        }
    }
}