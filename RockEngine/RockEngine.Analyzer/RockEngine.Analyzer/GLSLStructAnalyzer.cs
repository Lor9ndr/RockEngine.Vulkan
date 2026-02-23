using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace RockEngine.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class GLSLStructAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "RE001";
        private const string Category = "GPU.GLSLMemoryLayout";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "GLSL struct alignment issue",
            "{0}",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "GLSL structs must follow specific alignment rules for std140/std430/scalar layouts.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeStruct, SyntaxKind.StructDeclaration);
        }

        private void AnalyzeStruct(SyntaxNodeAnalysisContext context)
        {
            var structDecl = (StructDeclarationSyntax)context.Node;

            // Check if struct has GLSLStruct attribute
            if (!HasGLSLStructAttribute(structDecl))
                return;

            var semanticModel = context.SemanticModel;
            var structSymbol = semanticModel.GetDeclaredSymbol(structDecl);
            if (structSymbol == null) return;

            // Get layout from attribute
            var layout = GetLayoutFromAttribute(structDecl);

            // Analyze fields
            var fields = structDecl.Members.OfType<FieldDeclarationSyntax>().ToList();
            var fieldSymbols = new List<(IFieldSymbol Symbol, int Index)>();

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var variable = field.Declaration.Variables.FirstOrDefault();
                if (variable == null) continue;

                var fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol != null)
                    fieldSymbols.Add((fieldSymbol, i));
            }

            int currentOffset = 0;
            bool hasIssues = false;
            int structAlignment = GetStructBaseAlignment(layout);

            for (int i = 0; i < fieldSymbols.Count; i++)
            {
                var field = fieldSymbols[i];
                var fieldInfo = GetFieldInfo(field.Symbol.Type, layout);

                // Calculate expected alignment for this field
                int expectedAlignment = fieldInfo.Alignment;

                // For scalar layout, arrays have special handling
                if (layout == GLSLMemoryLayout.Scalar && field.Symbol.Type is IArrayTypeSymbol)
                {
                    expectedAlignment = 4; // Arrays align to 4 in scalar
                }

                // Calculate aligned offset
                int alignedOffset = Align(currentOffset, expectedAlignment);

                // Check alignment
                if (currentOffset != alignedOffset)
                {
                    var message = $"Field '{field.Symbol.Name}' at offset {currentOffset} is not aligned to {expectedAlignment} bytes (requires offset {alignedOffset})";
                    var diagnostic = Diagnostic.Create(Rule, field.Symbol.Locations[0], message);
                    context.ReportDiagnostic(diagnostic);
                    hasIssues = true;
                }

                // Check Vector3 in std140 - should be Vector4
                if (layout == GLSLMemoryLayout.Std140 && IsVector3Type(field.Symbol.Type))
                {
                    var message = $"Vector3 field '{field.Symbol.Name}' should be Vector4 in std140 layout (16 bytes instead of 12)";
                    var diagnostic = Diagnostic.Create(Rule, field.Symbol.Locations[0], message);
                    context.ReportDiagnostic(diagnostic);
                    hasIssues = true;
                }

                // Update current offset
                currentOffset = alignedOffset + fieldInfo.Size;

                // Align for next field
                if (i < fieldSymbols.Count - 1)
                {
                    var nextFieldInfo = GetFieldInfo(fieldSymbols[i + 1].Symbol.Type, layout);
                    currentOffset = Align(currentOffset, nextFieldInfo.Alignment);
                }
            }

            // Check struct size alignment
            int alignedStructSize = Align(currentOffset, structAlignment);
            if (currentOffset != alignedStructSize)
            {
                var message = layout switch
                {
                    GLSLMemoryLayout.Std140 => $"Struct size {currentOffset} is not a multiple of 16 bytes (std140 requirement)",
                    GLSLMemoryLayout.Std430 => $"Struct size {currentOffset} is not properly aligned for std430 layout",
                    GLSLMemoryLayout.Scalar => $"Struct size {currentOffset} is not a multiple of 4 bytes (scalar requirement)",
                    _ => $"Struct size {currentOffset} is not properly aligned"
                };
                var diagnostic = Diagnostic.Create(Rule, structDecl.Identifier.GetLocation(), message);
                context.ReportDiagnostic(diagnostic);
            }
        }

        private bool HasGLSLStructAttribute(StructDeclarationSyntax structDecl)
        {
            return structDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString() == "GLSLStruct" ||
                         a.Name.ToString() == "GLSLStructAttribute");
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
                    GLSLMemoryLayout.Std140 => Align(elementInfo.Size, 16), // Arrays stride rounded to 16 in std140
                    GLSLMemoryLayout.Std430 => elementInfo.Size,
                    GLSLMemoryLayout.Scalar => elementInfo.Size,
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
                    GLSLMemoryLayout.Scalar => (8, 4), // In scalar: size 8, alignment 4
                    _ => (8, 8)
                };
            }

            if (typeName.Contains("Vector3"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (16, 16), // std140: Vector3 takes 16 bytes, aligned to 16
                    GLSLMemoryLayout.Std430 => (12, 4),  // std430: Vector3 takes 12 bytes, aligned to 4
                    GLSLMemoryLayout.Scalar => (12, 4),  // scalar: Vector3 takes 12 bytes, aligned to 4
                    _ => (12, 4)
                };
            }

            if (typeName.Contains("Vector4"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (16, 16),
                    GLSLMemoryLayout.Std430 => (16, 16),
                    GLSLMemoryLayout.Scalar => (16, 4), // In scalar: size 16, alignment 4
                    _ => (16, 16)
                };
            }

            if (typeName.Contains("Matrix4x4"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (64, 16), // 4x Vector4 at 16 bytes each
                    GLSLMemoryLayout.Std430 => (64, 16),
                    GLSLMemoryLayout.Scalar => (64, 4),  // In scalar: matrices align to 4
                    _ => (64, 16)
                };
            }

            if (typeName.Contains("Matrix3x3"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (48, 16), // 3x Vector4 at 16 bytes each (padded)
                    GLSLMemoryLayout.Std430 => (48, 16),
                    GLSLMemoryLayout.Scalar => (36, 4),  // In scalar: 3x Vector3 at 12 bytes each
                    _ => (48, 16)
                };
            }

            if (typeName.Contains("Quaternion"))
            {
                return layout switch
                {
                    GLSLMemoryLayout.Std140 => (16, 16), // Treated as Vector4
                    GLSLMemoryLayout.Std430 => (16, 16),
                    GLSLMemoryLayout.Scalar => (16, 4),
                    _ => (16, 16)
                };
            }

            // Default: assume 4 bytes
            return (4, 4);
        }

        private bool IsVector3Type(ITypeSymbol type)
        {
            return type?.Name == "Vector3" ||
                   type?.ToString().Contains("System.Numerics.Vector3") == true;
        }

        private int GetStructBaseAlignment(GLSLMemoryLayout layout)
        {
            return layout switch
            {
                GLSLMemoryLayout.Std140 => 16,
                GLSLMemoryLayout.Std430 => 4, // std430 struct alignment is max of field alignments, min 4
                GLSLMemoryLayout.Scalar => 4, // scalar struct alignment is max of field alignments
                _ => 16
            };
        }

        private int Align(int offset, int alignment)
        {
            if (alignment <= 0) return offset;
            return ((offset + alignment - 1) / alignment) * alignment;
        }
    }
}