using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RockEngine.CodeGenerator
{
    [Generator]
    public class TraceSourceGenerator : IIncrementalGenerator
    {
        private const string TraceAttributeFullName = "RockEngine.Core.Attributes.TraceAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 1. Find all methods with at least one attribute
            var methodDecls = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                    transform: static (ctx, _) =>
                    {
                        var method = (MethodDeclarationSyntax)ctx.Node;
                        foreach (var attrList in method.AttributeLists)
                        {
                            foreach (var attr in attrList.Attributes)
                            {
                                if (attr.Name.ToString().Contains("Trace"))
                                    return method;
                            }
                        }
                        return null;
                    }
                )
                .Where(static m => m is not null)!;

            // 2. Combine with compilation to get semantic info
            var withSymbol = methodDecls
                .Combine(context.CompilationProvider)
                .Select(static (pair, ct) =>
                {
                    var methodDecl = pair.Left;
                    var compilation = pair.Right;
                    var model = compilation.GetSemanticModel(methodDecl.SyntaxTree);
                    var symbol = model.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
                    return (MethodDecl: methodDecl, Symbol: symbol);
                });

            // 3. Extract format, validate, and keep only valid entries
            var validated = withSymbol
                .Select(static (item, ct) =>
                {
                    var methodDecl = item.MethodDecl;
                    var methodSymbol = item.Symbol;
                    if (methodSymbol is null)
                        return (IsValid: false, Token: 0, Format: string.Empty, MethodSymbol: (IMethodSymbol?)null, Diagnostic: (Diagnostic?)null);

                    // Extract the format string
                    string? format = null;
                    foreach (var attr in methodSymbol.GetAttributes())
                    {
                        if (attr.AttributeClass?.ToDisplayString() == TraceAttributeFullName &&
                            attr.ConstructorArguments.Length > 0 &&
                            attr.ConstructorArguments[0].Value is string fmt)
                        {
                            format = fmt;
                            break;
                        }
                    }
                    if (format is null)
                        return (IsValid: false, Token: 0, Format: string.Empty, MethodSymbol: (IMethodSymbol?)null, Diagnostic: (Diagnostic?)null);

                    // Validate placeholders (with inheritance‑aware property lookup)
                    var diag = ValidateFormat(format, methodSymbol, methodDecl.GetLocation());
                    if (diag is not null)
                        return (IsValid: false, Token: 0, Format: string.Empty, MethodSymbol: (IMethodSymbol?)null, Diagnostic: diag);

                    int token = methodSymbol.OriginalDefinition.MetadataToken;
                    return (IsValid: true, Token: token, Format: format, MethodSymbol: (IMethodSymbol?)methodSymbol, Diagnostic: (Diagnostic?)null);
                });

            var validEntries = validated
                .Where(static x => x.IsValid)
                .Select(static (x, _) => (x.Token, x.Format, (IMethodSymbol)x.MethodSymbol!))
                .Collect();

            var errorDiagnostics = validated
                .Where(static x => x.Diagnostic is not null)
                .Select(static (x, _) => x.Diagnostic!)
                .Collect();

            context.RegisterSourceOutput(validEntries, static (ctx, entries) =>
            {
                if (!entries.IsEmpty)
                {
                    var source = GenerateFormatterClass(entries);
                    ctx.AddSource("TraceFormatters.g.cs", source);
                }
            });

            context.RegisterSourceOutput(errorDiagnostics, static (ctx, diags) =>
            {
                foreach (var diag in diags)
                    ctx.ReportDiagnostic(diag);
            });
        }

        /// <summary>Walks the inheritance chain to find a property.</summary>
        private static IPropertySymbol? GetPropertyIncludingBase(ITypeSymbol type, string name)
        {
            var current = type;
            while (current != null)
            {
                var prop = current.GetMembers().OfType<IPropertySymbol>()
                    .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (prop != null)
                    return prop;
                current = current.BaseType;
            }
            return null;
        }

        private static Diagnostic? ValidateFormat(string format, IMethodSymbol method, Location location)
        {
            var paramMap = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < method.Parameters.Length; i++)
                paramMap[method.Parameters[i].Name] = i;

            var regex = new Regex(@"\{([^}:]+)(?::([^}]*))?\}", RegexOptions.Compiled);
            foreach (Match m in regex.Matches(format))
            {
                string expression = m.Groups[1].Value.Trim();
                string[] chain = expression.Split('.');
                string rootParam = chain[0];

                if (!paramMap.TryGetValue(rootParam, out int rootIndex))
                {
                    return Diagnostic.Create(new DiagnosticDescriptor(
                        "TRACE001", "Invalid trace placeholder",
                        $"Parameter '{rootParam}' not found in method '{method.Name}'.",
                        "TraceAttribute", DiagnosticSeverity.Error, true),
                        location);
                }

                ITypeSymbol currentType = method.Parameters[rootIndex].Type;
                for (int i = 1; i < chain.Length; i++)
                {
                    var prop = GetPropertyIncludingBase(currentType, chain[i]);
                    if (prop is null)
                    {
                        return Diagnostic.Create(new DiagnosticDescriptor(
                            "TRACE002", "Invalid property chain",
                            $"Property '{chain[i]}' not found on type '{currentType.Name}' in '{{{expression}}}'.",
                            "TraceAttribute", DiagnosticSeverity.Error, true),
                            location);
                    }
                    currentType = prop.Type;
                }
            }
            return null;
        }

        private static string GenerateFormatterClass(ImmutableArray<(int Token, string Format, IMethodSymbol Method)> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using RockEngine.Core.Attributes;");
            sb.AppendLine();
            sb.AppendLine("internal static class TraceFormatterGenerated");
            sb.AppendLine("{");

            var formatMethodNames = new Dictionary<(IMethodSymbol Method, string Format), string>();
            var tokenToMethodName = new Dictionary<int, string>();
            int counter = 0; // fallback if token repeats

            foreach (var entry in entries)
            {
                var key = (entry.Method, entry.Format);
                if (!formatMethodNames.ContainsKey(key))
                {
                    // Unique name: combine token and format hash, with counter if needed
                    string name = $"Format_{entry.Token:X}_{entry.Format.GetHashCode():X}";
                    // Ensure no accidental collisions
                    while (formatMethodNames.Values.Contains(name))
                        name = $"Format_{entry.Token:X}_{entry.Format.GetHashCode():X}_{counter++}";
                    formatMethodNames[key] = name;
                }
                tokenToMethodName[entry.Token] = formatMethodNames[key];
            }

            // Generate each unique formatter
            foreach (var kvp in formatMethodNames)
            {
                GenerateFormatMethod(sb, kvp.Value, kvp.Key.Format, kvp.Key.Method);
            }

            // Lookup dictionary
            sb.AppendLine("    private static readonly System.Collections.Generic.Dictionary<int, Func<object[], string>> Lookup = new()");
            sb.AppendLine("    {");
            foreach (var kvp in tokenToMethodName)
                sb.AppendLine($"        {{ {kvp.Key}, {kvp.Value} }},");
            sb.AppendLine("    };");
            sb.AppendLine();
            sb.AppendLine("    public static Func<object[], string>? GetFormatter(int token)");
            sb.AppendLine("    {");
            sb.AppendLine("        Lookup.TryGetValue(token, out var f);");
            sb.AppendLine("        return f;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [ModuleInitializer]");
            sb.AppendLine("    internal static void Initialize()");
            sb.AppendLine("    {");
            sb.AppendLine("        TraceAttribute.FormatterLookup = GetFormatter;");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void GenerateFormatMethod(StringBuilder sb, string methodName, string format, IMethodSymbol method)
        {
            sb.AppendLine($"    private static string {methodName}(object[] args)");
            sb.AppendLine("    {");

            var paramMap = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < method.Parameters.Length; i++)
                paramMap[method.Parameters[i].Name] = i;

            var segments = ParseFormat(format);
            var parts = new List<string>();

            foreach (var seg in segments)
            {
                if (seg.IsLiteral)
                {
                    var escaped = seg.Text.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    parts.Add($"\"{escaped}\"");
                }
                else
                {
                    var expr = GeneratePlaceholderCode(method, paramMap, seg.Expression, seg.FormatSpecifier);
                    parts.Add(expr);
                }
            }

            if (parts.Count == 1)
                sb.AppendLine($"        return {parts[0]};");
            else
                sb.AppendLine($"        return string.Concat({string.Join(", ", parts)});");

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        private static string GeneratePlaceholderCode(
            IMethodSymbol method,
            Dictionary<string, int> paramMap,
            string expression,
            string? formatSpecifier)
        {
            string[] chain = expression.Split('.');
            string rootParam = chain[0];
            int rootIndex = paramMap[rootParam]; // validated

            var rootType = method.Parameters[rootIndex].Type;
            string castType = rootType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string rootAccess = $"(({castType})args[{rootIndex}])";

            var chainExpr = new StringBuilder(rootAccess);
            ITypeSymbol currentType = rootType;
            bool lastWasReference = rootType.IsReferenceType;

            for (int i = 1; i < chain.Length; i++)
            {
                var prop = GetPropertyIncludingBase(currentType, chain[i]);
                // If null, validation would have caught it; fallback
                if (prop is null)
                    return "\"<invalid>\"".Replace("\\", "\\\\").Replace("\"", "\\\"");
                chainExpr.Append(lastWasReference ? "?." : ".");
                chainExpr.Append(prop.Name);
                currentType = prop.Type;
                lastWasReference = currentType.IsReferenceType;
            }

            string finalExpr;
            if (!string.IsNullOrEmpty(formatSpecifier))
                finalExpr = $"string.Format(\"{{0:{formatSpecifier}}}\", {chainExpr})";
            else
                finalExpr = currentType.IsReferenceType
                    ? $"({chainExpr})?.ToString() ?? \"\""
                    : $"{chainExpr}.ToString()";

            return finalExpr;
        }

        private static List<Segment> ParseFormat(string format)
        {
            var segments = new List<Segment>();
            int lastIndex = 0;
            var regex = new Regex(@"\{([^}:]+)(?::([^}]*))?\}", RegexOptions.Compiled);

            foreach (Match m in regex.Matches(format))
            {
                if (m.Index > lastIndex)
                    segments.Add(new Segment(format.Substring(lastIndex, m.Index - lastIndex), true));

                string expression = m.Groups[1].Value.Trim();
                string? formatSpec = m.Groups[2].Success ? m.Groups[2].Value : null;
                segments.Add(new Segment(expression, false, formatSpec));

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < format.Length)
                segments.Add(new Segment(format.Substring(lastIndex), true));

            return segments;
        }

        private class Segment
        {
            public string Text { get; }
            public bool IsLiteral { get; }
            public string Expression { get; }
            public string? FormatSpecifier { get; }

            public Segment(string text, bool isLiteral, string? formatSpecifier = null)
            {
                Text = text;
                IsLiteral = isLiteral;
                Expression = text;
                FormatSpecifier = formatSpecifier;
            }
        }
    }
}