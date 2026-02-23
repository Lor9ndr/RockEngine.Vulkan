using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RockEngine.Analyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ComponentOnDrawUICodeFixProvider)), Shared]
    public class ComponentOnDrawUICodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(ComponentOnDrawUIAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() =>
            WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);
            if (root == null) return;

            foreach (var diagnostic in context.Diagnostics)
            {
                var classDecl = root.FindToken(diagnostic.Location.SourceSpan.Start)
                    .Parent?.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (classDecl == null) continue;

                var title = $"Add OnDrawUI method to '{classDecl.Identifier.Text}'";

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: title,
                        createChangedDocument: c => AddOnDrawUIMethodAsync(context.Document, classDecl, c),
                        equivalenceKey: diagnostic.Id),
                    diagnostic);
            }
        }

        private async Task<Document> AddOnDrawUIMethodAsync(
            Document document,
            ClassDeclarationSyntax classDecl,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            // Build the method: public void OnDrawUI(PropertyDrawer drawer)
            var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("drawer"))
                .WithType(SyntaxFactory.ParseTypeName("PropertyDrawer"));

            var body = SyntaxFactory.Block(
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("drawer"),
                            SyntaxFactory.IdentifierName("DrawComponentProperties")))
                    .WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(SyntaxFactory.ThisExpression()))))));

            var methodDecl = SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                    "OnDrawUI")
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(parameter)))
                .WithBody(body)
                .WithAdditionalAnnotations(Formatter.Annotation);

            // Add method to the class
            var newClassDecl = classDecl.AddMembers(methodDecl);

            // Ensure the class is partial (required by the generator)
            if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                var modifiers = classDecl.Modifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PartialKeyword));
                newClassDecl = newClassDecl.WithModifiers(modifiers);
            }

            var newRoot = root.ReplaceNode(classDecl, newClassDecl);

            // Ensure using for PropertyDrawer namespace
            newRoot = EnsureUsingDirective(newRoot, "RockEngine.Editor.EditorUI.ImGuiRendering");

            return document.WithSyntaxRoot(newRoot);
        }

        private SyntaxNode EnsureUsingDirective(SyntaxNode root, string namespaceName)
        {
            if (root is CompilationUnitSyntax compilationUnit)
            {
                if (!compilationUnit.Usings.Any(u => u.Name.ToString() == namespaceName))
                {
                    var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
                        .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                    compilationUnit = compilationUnit.AddUsings(usingDirective);
                    return compilationUnit;
                }
            }
            return root;
        }
    }
}