using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace RockEngine.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ThreadAffinityAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "RE0001";

        private static readonly LocalizableString Title =
            "Thread-affined type usage violation";
        private static readonly LocalizableString MessageFormat =
            "Thread-affined type '{0}' cannot be stored in {1}";
        private static readonly LocalizableString Description =
            "Thread-affined types must not be stored in fields or properties.";
        private const string Category = "Threading";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(
                GeneratedCodeAnalysisFlags.None);

            context.EnableConcurrentExecution();

            context.RegisterSymbolAction(
                AnalyzeFieldDeclaration,
                SymbolKind.Field);

            context.RegisterSymbolAction(
                AnalyzePropertyDeclaration,
                SymbolKind.Property);
            context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        }
        private void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;
            if (method.IsAsync && HasThreadAffinedParameter(method))
            {
                var diagnostic = Diagnostic.Create(
                    Rule,
                    method.Locations[0],
                    "UploadBatch",
                    "an async method");

                context.ReportDiagnostic(diagnostic);
            }
        }
        private bool HasThreadAffinedParameter(IMethodSymbol method)
        {
            foreach (var parameter in method.Parameters)
            {
                if (IsThreadAffinedType(parameter.Type))
                {
                    return true;
                }
            }
            return false;
        }


        private void AnalyzeFieldDeclaration(SymbolAnalysisContext context)
        {
            var field = (IFieldSymbol)context.Symbol;
            if (IsThreadAffinedType(field.Type))
            {
                var diagnostic = Diagnostic.Create(
                    Rule,
                    field.Locations[0],
                    field.Type.Name,
                    "a field");

                context.ReportDiagnostic(diagnostic);
            }
        }

        private void AnalyzePropertyDeclaration(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;
            if (IsThreadAffinedType(property.Type))
            {
                var diagnostic = Diagnostic.Create(
                    Rule,
                    property.Locations[0],
                    property.Type.Name,
                    "a property");

                context.ReportDiagnostic(diagnostic);
            }
        }

        private static bool IsThreadAffinedType(ITypeSymbol type)
        {
            return false; //(type?.Name == "UploadBatch" || type?.Name == "VkCommandBuffer") &&
                   //type.ContainingNamespace?.ToString() == "RockEngine.Vulkan";

        }
    }
}
