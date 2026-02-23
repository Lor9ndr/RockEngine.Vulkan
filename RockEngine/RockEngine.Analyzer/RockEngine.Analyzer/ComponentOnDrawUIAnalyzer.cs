using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using System.Collections.Immutable;
using System.Linq;

namespace RockEngine.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ComponentOnDrawUIAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "RE002";
        private static readonly LocalizableString Title = "Component missing OnDrawUI method";
        private static readonly LocalizableString MessageFormat = "Component '{0}' does not implement OnDrawUI method for custom UI rendering";
        private static readonly LocalizableString Description = "Components can provide custom UI rendering by implementing OnDrawUI(PropertyDrawer drawer).";
        private const string Category = "Design";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId, Title, MessageFormat, Category,
            DiagnosticSeverity.Info, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
        }

        private void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol == null) return;

            // Must be a component
            if (!IsComponent(classSymbol, context.Compilation)) return;

            // Find PropertyDrawer type (only present in editor assemblies)
            var propertyDrawerType = context.Compilation.GetTypeByMetadataName(
                "RockEngine.Editor.EditorUI.ImGuiRendering.PropertyDrawer");
            if (propertyDrawerType == null) return; // Not in editor context – skip

            // Check for existing OnDrawUI method with correct signature
            bool hasOnDrawUI = classSymbol.GetMembers().OfType<IMethodSymbol>()
                .Any(m => m.Name == "OnDrawUI" &&
                          m.Parameters.Length == 1 &&
                          SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, propertyDrawerType) &&
                          m.ReturnsVoid &&
                          m.DeclaredAccessibility == Accessibility.Public);

            if (!hasOnDrawUI)
            {
                var location = classDecl.Identifier.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, classSymbol.Name));
            }
        }

        private bool IsComponent(INamedTypeSymbol classSymbol, Compilation compilation)
        {
            if (classSymbol.IsAbstract) return false;

            // Implements IComponent?
            var iComponentType = compilation.GetTypeByMetadataName(
                "RockEngine.Core.ECS.Components.IComponent");
            if (iComponentType != null && classSymbol.AllInterfaces.Contains(iComponentType))
                return true;

            // Has ComponentAttribute?
            var componentAttr = classSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "ComponentAttribute");
            return componentAttr != null;
        }
    }
}

