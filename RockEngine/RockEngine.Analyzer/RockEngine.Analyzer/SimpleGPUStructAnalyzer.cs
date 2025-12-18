using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RockEngine.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class GLSLStructAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticIdA = "RE001A";
        public const string DiagnosticIdB = "RE001B";
        public const string DiagnosticIdC = "RE001C";
        public const string DiagnosticIdD = "RE001D";
        public const string DiagnosticIdE = "RE001E";
        private const string _category = "GPU.GLSLMemoryLayout";

        private static readonly DiagnosticDescriptor _glslAlignmentRule = new DiagnosticDescriptor(
            DiagnosticIdA,
            "GLSL struct misaligned",
            "GLSL struct '{0}' field '{1}' at offset {2} is not aligned to {3} bytes",
            _category,
            DiagnosticSeverity.Warning,
            true,
            "GLSL std140 layout requires specific alignment rules.");

        private static readonly DiagnosticDescriptor _vector3AsVec4Rule = new DiagnosticDescriptor(
            DiagnosticIdB,
            "Vector3 should be Vector4 for GLSL",
            "GLSL struct '{0}' field '{1}' should be Vector4 instead of Vector3",
            _category,
            DiagnosticSeverity.Warning,
            true,
            "In GLSL std140, vec3 is treated as vec4 (16 bytes).");

        private static readonly DiagnosticDescriptor _structSizeRule = new DiagnosticDescriptor(
            DiagnosticIdC,
            "GLSL struct size not aligned",
            "GLSL struct '{0}' size {1} is not aligned to 16 bytes",
            _category,
            DiagnosticSeverity.Warning,
            true,
            "GLSL structs must be aligned to 16 bytes for std140 layout.");

        private static readonly DiagnosticDescriptor _missingPackRule = new DiagnosticDescriptor(
            DiagnosticIdD,
            "Missing Pack attribute",
            "GLSL struct '{0}' should have [StructLayout(LayoutKind.Sequential, Pack = 16)]",
            _category,
            DiagnosticSeverity.Warning,
            true,
            "GLSL structs should use Pack=16 for proper alignment.");

        private static readonly DiagnosticDescriptor _matrixAlignmentRule = new DiagnosticDescriptor(
            DiagnosticIdE,
            "Matrix alignment error",
            "GLSL struct '{0}' Matrix4x4 at offset {1} is not aligned to 16 bytes",
            _category,
            DiagnosticSeverity.Warning,
            true,
            "mat4 in GLSL requires 16-byte alignment.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(_glslAlignmentRule, _vector3AsVec4Rule, _structSizeRule, _missingPackRule, _matrixAlignmentRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeStruct, SyntaxKind.StructDeclaration);
        }

        private void AnalyzeStruct(SyntaxNodeAnalysisContext context)
        {
            var structDeclaration = (StructDeclarationSyntax)context.Node;
            var structName = structDeclaration.Identifier.Text;

            if (!IsGPUStruct(structName, structDeclaration)) return;

            var semanticModel = context.SemanticModel;
            var fields = structDeclaration.Members.OfType<FieldDeclarationSyntax>().ToList();

            // Check for Pack=16 attribute
            CheckPackAttribute(structDeclaration, structName, context);

            // Analyze GLSL alignment
            int currentOffset = 0;
            int totalSize = 0;

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var variable = field.Declaration.Variables.FirstOrDefault();
                if (variable == null) continue;

                if (!(semanticModel.GetDeclaredSymbol(variable) is IFieldSymbol fieldSymbol)) continue;

                var fieldType = fieldSymbol.Type;
                var (size, alignment, glslType) = GetGLSLTypeInfo(fieldType);

                // Check alignment
                if (currentOffset % alignment != 0)
                {
                    var diagnostic = Diagnostic.Create(
                        _glslAlignmentRule,
                        field.GetLocation(),
                        structName,
                        fieldSymbol.Name,
                        currentOffset,
                        alignment);
                    context.ReportDiagnostic(diagnostic);
                }

                // Check Vector3 -> should be Vector4
                if (IsVector3Type(fieldType))
                {
                    var diagnostic = Diagnostic.Create(
                        _vector3AsVec4Rule,
                        field.GetLocation(),
                        structName,
                        fieldSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                }

                // Check Matrix4x4 alignment
                if (IsMatrix4x4Type(fieldType) && currentOffset % 16 != 0)
                {
                    var diagnostic = Diagnostic.Create(
                        _matrixAlignmentRule,
                        field.GetLocation(),
                        structName,
                        fieldSymbol.Name,
                        currentOffset);
                    context.ReportDiagnostic(diagnostic);
                }

                // Update offsets
                currentOffset += size;
                totalSize = currentOffset;

                // Align for next field
                currentOffset = AlignTo(currentOffset, alignment);
            }

            // Check total size alignment
            if (totalSize % 16 != 0)
            {
                var diagnostic = Diagnostic.Create(
                    _structSizeRule,
                    structDeclaration.Identifier.GetLocation(),
                    structName,
                    totalSize);
                context.ReportDiagnostic(diagnostic);
            }
        }

        private void CheckPackAttribute(StructDeclarationSyntax structDecl, string structName, SyntaxNodeAnalysisContext context)
        {
            var hasPack16 = structDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.ToString().Contains("Pack = 16") || a.ToString().Contains("Pack=16"));

            if (!hasPack16)
            {
                var diagnostic = Diagnostic.Create(
                    _missingPackRule,
                    structDecl.Identifier.GetLocation(),
                    structName);
                context.ReportDiagnostic(diagnostic);
            }
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
                return (16, 16, "vec4"); // В GLSL vec3 занимает как vec4!
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

        private int AlignTo(int offset, int alignment)
        {
            return (offset + alignment - 1) & ~(alignment - 1);
        }

        private bool IsGPUStruct(string structName, StructDeclarationSyntax syntax)
        {
            return structName.StartsWith("GPU") ||
                   structName.Contains("Physics") ||
                   syntax.AttributeLists.SelectMany(a => a.Attributes)
                         .Any(attr => attr.Name.ToString().Contains("StructLayout"));
        }

        private bool IsVector3Type(ITypeSymbol typeSymbol)
        {
            return typeSymbol.Name == "Vector3" ||
                   typeSymbol.ToString().Contains("System.Numerics.Vector3");
        }

        private bool IsMatrix4x4Type(ITypeSymbol typeSymbol)
        {
            return typeSymbol.Name == "Matrix4x4" ||
                   typeSymbol.ToString().Contains("System.Numerics.Matrix4x4");
        }
    }
}