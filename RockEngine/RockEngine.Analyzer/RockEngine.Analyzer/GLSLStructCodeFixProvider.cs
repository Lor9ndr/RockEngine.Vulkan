using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RockEngine.Analyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GLSLStructCodeFixProvider)), Shared]
    public class GLSLStructCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create("RE001");

        public sealed override FixAllProvider GetFixAllProvider() =>
            WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            if (root == null) return;

            foreach (var diagnostic in context.Diagnostics)
            {
                var structDecl = GetContainingStruct(root, diagnostic.Location.SourceSpan);
                if (structDecl == null) continue;

                var title = $"Fix GLSL alignment in '{structDecl.Identifier.Text}'";

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: title,
                        createChangedDocument: c => FixStructAsync(context.Document, structDecl, diagnostic, c),
                        equivalenceKey: diagnostic.Id),
                    diagnostic);
            }
        }

        private async Task<Document> FixStructAsync(Document document,
            StructDeclarationSyntax structDecl,
            Diagnostic diagnostic,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (root == null || semanticModel == null) return document;

            // Get layout from attribute
            var layout = GetLayoutFromAttribute(structDecl);

            // Analyze and fix the struct
            var newMembers = await FixStructFieldsAsync(structDecl, semanticModel, layout, cancellationToken);

            // Create new struct
            var newStruct = structDecl.WithMembers(SyntaxFactory.List(newMembers))
                .WithAdditionalAnnotations(Formatter.Annotation);

            var newRoot = root.ReplaceNode(structDecl, newStruct);

            // Ensure System.Numerics is available
            newRoot = EnsureSystemNumericsUsing(newRoot);

            return document.WithSyntaxRoot(newRoot);
        }

        private async Task<List<MemberDeclarationSyntax>> FixStructFieldsAsync(
            StructDeclarationSyntax structDecl,
            SemanticModel semanticModel,
            GLSLMemoryLayout layout,
            CancellationToken cancellationToken)
        {
            var fields = structDecl.Members.OfType<FieldDeclarationSyntax>().ToList();
            var newMembers = new List<MemberDeclarationSyntax>();
            int currentOffset = 0;
            int paddingCounter = 1;

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var variable = field.Declaration.Variables.FirstOrDefault();
                if (variable == null) continue;

                var fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol == null) continue;

                var fieldInfo = GetFieldInfo(fieldSymbol.Type, layout);
                int expectedAlignment = fieldInfo.Alignment;

                // For scalar layout, arrays align to 4
                if (layout == GLSLMemoryLayout.Scalar && fieldSymbol.Type is IArrayTypeSymbol)
                {
                    expectedAlignment = 4;
                }

                // Add padding if needed
                int alignedOffset = Align(currentOffset, expectedAlignment);
                if (currentOffset != alignedOffset)
                {
                    int paddingNeeded = alignedOffset - currentOffset;
                    var paddingFields = CreatePaddingFields(paddingNeeded, ref paddingCounter, layout);
                    newMembers.AddRange(paddingFields);
                    currentOffset = alignedOffset;
                }

                // Handle Vector3 in std140 - convert to Vector4
                if (layout == GLSLMemoryLayout.Std140 && IsVector3Type(fieldSymbol.Type))
                {
                    var vector4Field = ConvertToVector4(field);
                    newMembers.Add(vector4Field);
                    currentOffset += 16; // Vector4 size in std140
                }
                else
                {
                    newMembers.Add(field);
                    currentOffset += fieldInfo.Size;
                }

                // Align for next field if there is one
                if (i < fields.Count - 1)
                {
                    var nextField = fields[i + 1];
                    var nextVariable = nextField.Declaration.Variables.FirstOrDefault();
                    if (nextVariable != null)
                    {
                        var nextFieldSymbol = semanticModel.GetDeclaredSymbol(nextVariable) as IFieldSymbol;
                        if (nextFieldSymbol != null)
                        {
                            var nextFieldInfo = GetFieldInfo(nextFieldSymbol.Type, layout);
                            currentOffset = Align(currentOffset, nextFieldInfo.Alignment);
                        }
                    }
                }
            }

            // Add final padding for struct alignment
            int structAlignment = GetStructBaseAlignment(layout);
            int alignedStructSize = Align(currentOffset, structAlignment);
            if (currentOffset != alignedStructSize)
            {
                int finalPadding = alignedStructSize - currentOffset;
                var paddingFields = CreatePaddingFields(finalPadding, ref paddingCounter, layout);
                newMembers.AddRange(paddingFields);
            }

            // Add non-field members back
            var otherMembers = structDecl.Members
                .Where(m => !(m is FieldDeclarationSyntax))
                .ToList();
            newMembers.AddRange(otherMembers);

            return newMembers;
        }

        private GLSLMemoryLayout GetLayoutFromAttribute(StructDeclarationSyntax structDecl)
        {
            var glslAttribute = structDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(a => a.Name.ToString() == "GLSLStruct" ||
                                    a.Name.ToString() == "GLSLStructAttribute");

            if (glslAttribute?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax arg)
            {
                if (arg.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var layoutName = memberAccess.Name.Identifier.Text;
                    if (Enum.TryParse(layoutName, out GLSLMemoryLayout layout))
                    {
                        return layout;
                    }
                }
            }

            return GLSLMemoryLayout.Std140;
        }

        private bool IsVector3Type(ITypeSymbol type)
        {
            return type?.Name == "Vector3" ||
                   type?.ToString().Contains("System.Numerics.Vector3") == true;
        }

        private (int Size, int Alignment) GetFieldInfo(ITypeSymbol type, GLSLMemoryLayout layout)
        {
            if (type == null) return (4, 4);

            var typeName = type.ToString();

            // Handle arrays
            if (type is IArrayTypeSymbol arrayType)
            {
                var elementInfo = GetFieldInfo(arrayType.ElementType, layout);
                int arrayStride = layout switch
                {
                    GLSLMemoryLayout.Std140 => Align(elementInfo.Size, 16),
                    _ => elementInfo.Size
                };
                return (arrayStride, elementInfo.Alignment);
            }

            // Basic types
            if (type.SpecialType == SpecialType.System_Single ||
                type.SpecialType == SpecialType.System_Int32 ||
                type.SpecialType == SpecialType.System_UInt32 ||
                type.SpecialType == SpecialType.System_Boolean)
            {
                return (4, 4);
            }

            if (typeName.Contains("Vector2"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (8, 8),
                    GLSLMemoryLayout.Std430 => (8, 8),
                    GLSLMemoryLayout.Scalar => (8, 4),
                    _ => (8, 8)
                };
            }

            if (typeName.Contains("Vector3"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (16, 16),
                    GLSLMemoryLayout.Std430 => (12, 4),
                    GLSLMemoryLayout.Scalar => (12, 4),
                    _ => (12, 4)
                };
            }

            if (typeName.Contains("Vector4"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (16, 16),
                    GLSLMemoryLayout.Std430 => (16, 16),
                    GLSLMemoryLayout.Scalar => (16, 4),
                    _ => (16, 16)
                };
            }

            if (typeName.Contains("Matrix4x4"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (64, 16),
                    GLSLMemoryLayout.Std430 => (64, 16),
                    GLSLMemoryLayout.Scalar => (64, 4),
                    _ => (64, 16)
                };
            }

            if (typeName.Contains("Quaternion"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (16, 16),
                    GLSLMemoryLayout.Std430 => (16, 16),
                    GLSLMemoryLayout.Scalar => (16, 4),
                    _ => (16, 16)
                };
            }

            return (4, 4);
        }

        private int GetStructBaseAlignment(GLSLMemoryLayout layout)
        {
            return layout switch
            {
                GLSLMemoryLayout.Std140 => 16,
                _ => 4
            };
        }

        private List<FieldDeclarationSyntax> CreatePaddingFields(int paddingNeeded, ref int counter, GLSLMemoryLayout layout)
        {
            var paddingFields = new List<FieldDeclarationSyntax>();

            // Use float fields for padding (4 bytes each)
            while (paddingNeeded >= 4)
            {
                paddingFields.Add(CreateFloatField($"_padding{counter++}"));
                paddingNeeded -= 4;
            }

            // For remaining bytes (shouldn't happen with proper alignment)
            if (paddingNeeded > 0)
            {
                paddingFields.Add(CreateByteField($"_padding{counter++}"));
            }

            return paddingFields;
        }

        private FieldDeclarationSyntax CreateFloatField(string name)
        {
            return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.FloatKeyword)))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(name))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
        }

        private FieldDeclarationSyntax CreateByteField(string name)
        {
            return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(name))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
        }

        private FieldDeclarationSyntax ConvertToVector4(FieldDeclarationSyntax vector3Field)
        {
            var vector4Type = SyntaxFactory.ParseTypeName("System.Numerics.Vector4");
            return vector3Field.WithDeclaration(
                vector3Field.Declaration.WithType(vector4Type));
        }

        private int Align(int offset, int alignment)
        {
            if (alignment <= 0) return offset;
            return ((offset + alignment - 1) / alignment) * alignment;
        }

        private SyntaxNode EnsureSystemNumericsUsing(SyntaxNode root)
        {
            if (root is CompilationUnitSyntax compilationUnit)
            {
                if (!compilationUnit.Usings.Any(u => u.Name.ToString() == "System.Numerics"))
                {
                    var usingDirective = SyntaxFactory.UsingDirective(
                        SyntaxFactory.ParseName("System.Numerics"));
                    return compilationUnit.AddUsings(usingDirective);
                }
            }
            return root;
        }

        private StructDeclarationSyntax GetContainingStruct(SyntaxNode root, TextSpan span)
        {
            var node = root.FindNode(span);
            return node.AncestorsAndSelf().OfType<StructDeclarationSyntax>().FirstOrDefault();
        }
    }
}