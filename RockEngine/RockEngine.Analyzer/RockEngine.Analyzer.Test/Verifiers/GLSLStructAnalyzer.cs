using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;

namespace RockEngine.Analyzer.Test.Verifiers
{
    [TestClass]
    public class GLSLStructAnalyzerTests : CSharpAnalyzerTest<GLSLStructAnalyzer, DefaultVerifier>
    {

        [TestMethod]
        public async Task TestStructWithoutAttribute_NoDiagnostics()
        {
            var test = @"
public struct TestStruct
{
    public float Value;
    public int Number;
}";

            await VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task TestStd140Vector3_WarningAndFix()
        {
            var test = @"
using RockEngine.Analyzer;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct GridPushConstants
{
    public System.Numerics.Vector3 cameraPosition;
    public float gridScale;
    public System.Numerics.Matrix4x4 viewProj;
    public System.Numerics.Matrix4x4 model;
}";

            var expectedDiagnostics = new[]
            {
                DiagnosticResult.CompilerWarning("RE001")
                    .WithMessage("Vector3 field 'cameraPosition' should be Vector4 in std140 layout (16 bytes instead of 12)")
                    .WithLocation(7, 5),
                DiagnosticResult.CompilerWarning("RE001")
                    .WithMessage("Field 'gridScale' at offset 16 is not aligned to 4 bytes")
                    .WithLocation(8, 5),
                DiagnosticResult.CompilerWarning("RE001")
                    .WithMessage("Field 'viewProj' at offset 20 is not aligned to 16 bytes")
                    .WithLocation(9, 5),
                DiagnosticResult.CompilerWarning("RE001")
                    .WithMessage("Struct size 148 is not a multiple of 16 bytes (std140 requirement)")
                    .WithLocation(5, 1)
            };

            var fixedCode = @"
using RockEngine.Analyzer;
using System.Numerics;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct GridPushConstants
{
    public Vector4 cameraPosition;
    public float gridScale;
    private float _padding1;
    private float _padding2;
    private float _padding3;
    public Matrix4x4 viewProj;
    public Matrix4x4 model;
    private float _padding4;
    private float _padding5;
    private float _padding6;
    private float _padding7;
}";

            await VerifyCodeFixAsync(test, expectedDiagnostics, fixedCode);
        }

        [TestMethod]
        public async Task TestStd430Vector3_NoWarning()
        {
            var test = @"
using RockEngine.Analyzer;

[GLSLStruct(GLSLMemoryLayout.Std430)]
public struct Std430Struct
{
    public System.Numerics.Vector3 position;
    public float scale;
}";

            // In std430, Vector3 is 12 bytes, so no warning
            await VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task TestProperlyAlignedStruct_NoDiagnostics()
        {
            var test = @"
using RockEngine.Analyzer;
using System.Numerics;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct ProperlyAligned
{
    public Vector4 position;
    public Vector4 direction;
    public Matrix4x4 view;
    public Matrix4x4 proj;
    public float near;
    public float far;
    private Vector2 _padding;
}";

            await VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task TestMultipleVector3s_Fix()
        {
            var test = @"
using RockEngine.Analyzer;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct MultiVector3
{
    public System.Numerics.Vector3 v1;
    public System.Numerics.Vector3 v2;
    public System.Numerics.Vector3 v3;
}";

            var fixedCode = @"
using RockEngine.Analyzer;
using System.Numerics;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct MultiVector3
{
    public Vector4 v1;
    public Vector4 v2;
    public Vector4 v3;
    private Vector4 _padding1;
}";

            await VerifyCodeFixAsync(test, DiagnosticResult.CompilerWarning("RE001"), fixedCode);
        }

        [TestMethod]
        public async Task TestMixedTypesWithPadding()
        {
            var test = @"
using RockEngine.Analyzer;
using System.Numerics;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct MixedTypes
{
    public float a;
    public Vector2 b;
    public int c;
    public Matrix4x4 d;
}";

            var expectedDiagnostics = new[]
            {
                DiagnosticResult.CompilerWarning("RE001")
                    .WithMessage("Field 'c' at offset 12 is not aligned to 4 bytes")
                    .WithLocation(11, 5),
                DiagnosticResult.CompilerWarning("RE001")
                    .WithMessage("Field 'd' at offset 16 is not aligned to 16 bytes")
                    .WithLocation(12, 5)
            };

            var fixedCode = @"
using RockEngine.Analyzer;
using System.Numerics;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct MixedTypes
{
    public float a;
    public Vector2 b;
    private float _padding1;
    public int c;
    public Matrix4x4 d;
}";

            await VerifyCodeFixAsync(test, expectedDiagnostics, fixedCode);
        }

        [TestMethod]
        public async Task TestStructWithMethods_KeepsMethods()
        {
            var test = @"
using RockEngine.Analyzer;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct StructWithMethods
{
    public System.Numerics.Vector3 position;
    
    public void Method1() { }
    public int Property { get; set; }
}";

            var fixedCode = @"
using RockEngine.Analyzer;
using System.Numerics;

[GLSLStruct(GLSLMemoryLayout.Std140)]
public struct StructWithMethods
{
    public Vector4 position;
    private float _padding1;
    private float _padding2;
    private float _padding3;
    
    public void Method1() { }
    public int Property { get; set; }
}";

            await VerifyCodeFixAsync(test, DiagnosticResult.CompilerWarning("RE001"), fixedCode);
        }

        private async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
        {
            var test = new CSharpAnalyzerTest<GLSLStructAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net60
            };

            test.TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(GLSLStructAttribute).Assembly.Location));

            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync(CancellationToken.None);
        }

        private async Task VerifyCodeFixAsync(
            string source,
            DiagnosticResult expectedDiagnostic,
            string fixedSource)
        {
            await VerifyCodeFixAsync(source, new[] { expectedDiagnostic }, fixedSource);
        }

        private async Task VerifyCodeFixAsync(
            string source,
            DiagnosticResult[] expectedDiagnostics,
            string fixedSource)
        {
            var test = new CSharpCodeFixTest<GLSLStructAnalyzer, GLSLStructCodeFixProvider, DefaultVerifier>
            {
                TestCode = source,
                FixedCode = fixedSource,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net100
            };

            test.TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(GLSLStructAttribute).Assembly.Location));
            test.TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(System.Numerics.Vector3).Assembly.Location));

            test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
            await test.RunAsync(CancellationToken.None);
        }
    }
}