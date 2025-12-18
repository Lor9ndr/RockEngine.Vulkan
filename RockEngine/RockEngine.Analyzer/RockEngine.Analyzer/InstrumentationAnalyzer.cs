using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using System.Collections.Immutable;
using System.Linq;

namespace RockEngine.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class InstrumentationAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "RCK0000";

        private static readonly LocalizableString Title = "Method can be instrumented";
        private static readonly LocalizableString MessageFormat = "Method '{0}' has [Instrument] attribute";
        private static readonly LocalizableString Description = "Methods with [Instrument] attribute can be automatically instrumented for diagnostics.";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            "Instrumentation",
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Register for method declarations
            context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        }

        private void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;

            // Check if method has [Instrument] attribute
            var hasInstrumentAttribute = methodDeclaration.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr =>
                    attr.Name.ToString().Contains("Instrument") ||
                    (context.SemanticModel.GetSymbolInfo(attr).Symbol is IMethodSymbol attributeSymbol &&
                     attributeSymbol.ContainingType.Name.Contains("Instrument")));

            if (hasInstrumentAttribute)
            {
                var diagnostic = Diagnostic.Create(
                    Rule,
                    methodDeclaration.Identifier.GetLocation(),
                    methodDeclaration.Identifier.Text);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}