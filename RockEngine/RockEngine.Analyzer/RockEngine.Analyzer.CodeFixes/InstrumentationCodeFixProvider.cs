using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RockEngine.Analyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InstrumentationCodeFixProvider)), Shared]
    public class InstrumentationCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(InstrumentationAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the method declaration at the diagnostic location
            var methodDecl = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                .OfType<MethodDeclarationSyntax>().FirstOrDefault();

            if (methodDecl == null) return;

            // Register a code fix that will instrument the method
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add instrumentation",
                    createChangedDocument: c => InstrumentMethodAsync(context.Document, methodDecl, c),
                    equivalenceKey: "AddInstrumentation"),
                diagnostic);
        }

        private async Task<Document> InstrumentMethodAsync(
            Document document,
            MethodDeclarationSyntax methodDecl,
            CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            // Create instrumentation wrapper
            var instrumentedBody = CreateInstrumentedBody(methodDecl);

            // Replace the method body with instrumented version
            editor.ReplaceNode(methodDecl, methodDecl.WithBody(instrumentedBody));

            return editor.GetChangedDocument();
        }

        private BlockSyntax CreateInstrumentedBody(MethodDeclarationSyntax methodDecl)
        {
            var methodName = methodDecl.Identifier.Text;
            var originalBody = methodDecl.Body ?? SyntaxFactory.Block();

            // Create variable declaration: var marker = DiagnosticsCollector.Current?.CreateMarker("MethodName", "Method")
            var variableDeclaration = SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(
                        SyntaxFactory.Identifier("marker"),
                        null,
                        SyntaxFactory.EqualsValueClause(
                            CreateCreateMarkerInvocation(methodName)))));

            // Create using statement
            var usingStatement = SyntaxFactory.UsingStatement(
                variableDeclaration,
                expression: null,
                statement: originalBody);

            // Create block with the using statement
            return SyntaxFactory.Block(usingStatement)
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        private InvocationExpressionSyntax CreateCreateMarkerInvocation(string methodName)
        {
            // DiagnosticsCollector.Current?.CreateMarker("methodName", "Method")
            var currentMemberAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("DiagnosticsCollector"),
                SyntaxFactory.IdentifierName("Current"));

            var conditionalAccess = SyntaxFactory.ConditionalAccessExpression(
                currentMemberAccess,
                SyntaxFactory.MemberBindingExpression(
                    SyntaxFactory.IdentifierName("CreateMarker")));

            return SyntaxFactory.InvocationExpression(
                conditionalAccess,
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(new[]
                    {
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal(methodName))),
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal("Method")))
                    })));
        }
    }
}