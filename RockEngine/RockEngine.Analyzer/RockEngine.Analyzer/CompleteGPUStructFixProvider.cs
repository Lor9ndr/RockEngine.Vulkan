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
            ImmutableArray.Create(
                "RE001A", // GLSL alignment
                "RE001B", // Vector3 should be Vector4
                "RE001C", // Struct size
                "RE001D", // Missing Pack
                "RE001E"); // Matrix alignment

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            if (root == null) return;

            foreach (var diagnostic in context.Diagnostics)
            {
                var structDecl = GetContainingStruct(root, diagnostic.Location.SourceSpan);
                if (structDecl == null) continue;

                var title = diagnostic.Id switch
                {
                    "RE001A" => $"Fix GLSL alignment in '{structDecl.Identifier.Text}'",
                    "RE001B" => $"Convert Vector3 to Vector4 in '{structDecl.Identifier.Text}'",
                    "RE001C" => $"Align struct size to 16 bytes in '{structDecl.Identifier.Text}'",
                    "RE001D" => $"Add Pack=16 attribute to '{structDecl.Identifier.Text}'",
                    "RE001E" => $"Fix Matrix4x4 alignment in '{structDecl.Identifier.Text}'",
                    _ => $"Fix GLSL issue in '{structDecl.Identifier.Text}'"
                };

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: title,
                        createChangedDocument: c => ConvertToGLSLCompatibleAsync(context.Document, structDecl, diagnostic, c),
                        equivalenceKey: $"GLSLFix_{structDecl.Identifier.Text}"),
                    diagnostic);
            }
        }

        private async Task<Document> ConvertToGLSLCompatibleAsync(Document document,
            StructDeclarationSyntax structDecl,
            Diagnostic diagnostic,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (root == null || semanticModel == null) return document;

            // Конвертируем структуру в GLSL-совместимую
            var newStruct = await ConvertToGLSLStructAsync(structDecl, semanticModel, cancellationToken);
            var newRoot = root.ReplaceNode(structDecl, newStruct);

            // Добавляем using для System.Runtime.InteropServices если нужно
            var compilationUnit = newRoot as CompilationUnitSyntax;
            if (compilationUnit != null)
            {
                if (!compilationUnit.Usings.Any(u => u.Name.ToString() == "System.Runtime.InteropServices"))
                {
                    var usingDirective = SyntaxFactory.UsingDirective(
                        SyntaxFactory.ParseName("System.Runtime.InteropServices"));
                    newRoot = compilationUnit.AddUsings(usingDirective);
                }
            }

            return document.WithSyntaxRoot(newRoot);
        }

        private async Task<StructDeclarationSyntax> ConvertToGLSLStructAsync(
            StructDeclarationSyntax structDecl,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var fields = structDecl.Members.OfType<FieldDeclarationSyntax>().ToList();
            var newMembers = new List<MemberDeclarationSyntax>();

            // Добавляем Pack=16 атрибут
            var newStruct = AddPack16Attribute(structDecl);

            // Конвертируем поля
            int currentOffset = 0;
            int paddingCounter = 1;

            foreach (var field in fields)
            {
                var variable = field.Declaration.Variables.FirstOrDefault();
                if (variable == null) continue;

                if (!(semanticModel.GetDeclaredSymbol(variable) is IFieldSymbol fieldSymbol)) continue;

                var fieldType = fieldSymbol.Type;
                var (size, alignment, _) = GetGLSLTypeInfo(fieldType);

                // Проверяем выравнивание и добавляем padding если нужно
                if (currentOffset % alignment != 0)
                {
                    int paddingNeeded = alignment - (currentOffset % alignment);
                    var paddingField = CreateGLSLPadding(paddingNeeded, ref paddingCounter);
                    newMembers.Add(paddingField);
                    currentOffset += paddingNeeded;
                }

                // Конвертируем Vector3 в Vector4 для GLSL
                if (IsVector3Type(fieldType))
                {
                    var vector4Field = ConvertVector3ToVector4(field, fieldSymbol);
                    newMembers.Add(vector4Field);
                }
                else
                {
                    newMembers.Add(field);
                }

                currentOffset += size;

                // Выравниваем для следующего поля
                currentOffset = AlignTo(currentOffset, alignment);
            }

            // Добавляем финальный padding для выравнивания размера структуры
            int finalPadding = (16 - (currentOffset % 16)) % 16;
            if (finalPadding > 0)
            {
                var paddingField = CreateGLSLPadding(finalPadding, ref paddingCounter);
                newMembers.Add(paddingField);
            }

            // Добавляем не-полевые члены
            var otherMembers = structDecl.Members
                .Where(m => !(m is FieldDeclarationSyntax))
                .ToList();
            newMembers.AddRange(otherMembers);

            return newStruct
                .WithMembers(SyntaxFactory.List(newMembers))
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        private StructDeclarationSyntax AddPack16Attribute(StructDeclarationSyntax structDecl)
        {
            // Создаем атрибут StructLayout с Pack=16
            var structLayoutAttribute = SyntaxFactory.Attribute(
                SyntaxFactory.ParseName("StructLayout"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SeparatedList(new[]
                    {
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName("LayoutKind"),
                                SyntaxFactory.IdentifierName("Sequential"))),
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName("Pack"),
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(16))))
                    })));

            var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(structLayoutAttribute));

            // Удаляем старые StructLayout атрибуты
            var existingAttributes = structDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Where(a => !a.Name.ToString().Contains("StructLayout"))
                .ToList();

            if (existingAttributes.Count > 0)
            {
                var additionalAttributeList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SeparatedList(existingAttributes));
                return structDecl.WithAttributeLists(
                    SyntaxFactory.List(new[] { attributeList, additionalAttributeList }));
            }

            return structDecl.WithAttributeLists(SyntaxFactory.SingletonList(attributeList));
        }

        private FieldDeclarationSyntax ConvertVector3ToVector4(
            FieldDeclarationSyntax vector3Field,
            IFieldSymbol fieldSymbol)
        {
            // Меняем тип с Vector3 на Vector4
            var vector4Type = SyntaxFactory.ParseTypeName("System.Numerics.Vector4");

            return vector3Field
                .WithDeclaration(
                    vector3Field.Declaration.WithType(vector4Type))
                .WithModifiers(vector3Field.Modifiers);
        }

        private FieldDeclarationSyntax CreateGLSLPadding(int sizeInBytes, ref int counter)
        {
            // Создаем поле-заполнитель для GLSL выравнивания
            string fieldName = $"_glslPadding{counter++}";

            return sizeInBytes switch
            {
                4 => CreateFloatPadding(fieldName),
                8 => CreateVector2Padding(fieldName),
                12 => CreateVector3Padding(fieldName),
                16 => CreateVector4Padding(fieldName),
                _ => CreateByteArrayPadding(sizeInBytes, fieldName)
            };
        }

        private FieldDeclarationSyntax CreateFloatPadding(string fieldName)
        {
            return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.FloatKeyword)))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(fieldName)))))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
        }

        private FieldDeclarationSyntax CreateVector2Padding(string fieldName)
        {
            return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ParseTypeName("System.Numerics.Vector2"))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(fieldName)))))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
        }

        private FieldDeclarationSyntax CreateVector3Padding(string fieldName)
        {
            return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ParseTypeName("System.Numerics.Vector3"))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(fieldName)))))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
        }

        private FieldDeclarationSyntax CreateVector4Padding(string fieldName)
        {
            return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ParseTypeName("System.Numerics.Vector4"))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(fieldName)))))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
        }

        private FieldDeclarationSyntax CreateByteArrayPadding(int size, string fieldName)
        {
            return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ArrayType(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                    .WithRankSpecifiers(
                        SyntaxFactory.SingletonList(
                            SyntaxFactory.ArrayRankSpecifier(
                                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.NumericLiteralExpression,
                                        SyntaxFactory.Literal(size)))))))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(fieldName)))))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
        }

        private (int size, int alignment, string glslType) GetGLSLTypeInfo(ITypeSymbol typeSymbol)
        {
            var typeName = typeSymbol.ToString();

            if (typeSymbol.SpecialType == SpecialType.System_Single)
                return (4, 4, "float");
            else if (typeSymbol.SpecialType == SpecialType.System_UInt32 ||
                     typeSymbol.SpecialType == SpecialType.System_Int32)
                return (4, 4, "int/uint");
            else if (typeName.Contains("Vector3"))
                return (16, 16, "vec4");
            else if (typeName.Contains("Vector4"))
                return (16, 16, "vec4");
            else if (typeName.Contains("Vector2"))
                return (8, 8, "vec2");
            else if (typeName.Contains("Matrix4x4"))
                return (64, 16, "mat4");
            else if (typeName.Contains("Quaternion"))
                return (16, 16, "vec4");
            else
                return (4, 4, "float");
        }

        private bool IsVector3Type(ITypeSymbol typeSymbol)
        {
            return typeSymbol.Name == "Vector3" ||
                   typeSymbol.ToString().Contains("System.Numerics.Vector3");
        }

        private int AlignTo(int offset, int alignment)
        {
            return (offset + alignment - 1) & ~(alignment - 1);
        }

        private StructDeclarationSyntax GetContainingStruct(SyntaxNode root, TextSpan span)
        {
            return root.FindNode(span).FirstAncestorOrSelf<StructDeclarationSyntax>();
        }
    }
}