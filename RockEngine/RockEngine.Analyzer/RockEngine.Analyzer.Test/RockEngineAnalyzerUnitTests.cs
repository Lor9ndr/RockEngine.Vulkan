using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Threading.Tasks;

using VerifyCS = RockEngine.Analyzer.Test.CSharpCodeFixVerifier<
    RockEngine.Analyzer.ThreadAffinityAnalyzer,
    RockEngine.Analyzer.RockEngineAnalyzerCodeFixProvider>;

namespace RockEngine.Analyzer.Test
{[TestClass]
    public class ThreadAffinityAnalyzerTests
    {
        [TestMethod]
        public async Task FieldDeclaration_ShouldTriggerDiagnostic()
        {
            const string testCode = @"
using RockEngine.Vulkan;

namespace TestNamespace
{
    public class TestClass
    {
        private UploadBatch {|#0:_batch|};
    }
}";

            var expected = VerifyCS.Diagnostic("RE0001")
                .WithLocation(0)
                .WithArguments("UploadBatch", "a field");
                
            await VerifyCS.VerifyAnalyzerAsync(testCode, expected);
        }

        [TestMethod]
        public async Task PropertyDeclaration_ShouldTriggerDiagnostic()
        {
            const string testCode = @"
using RockEngine.Vulkan;

namespace TestNamespace
{
    public class TestClass
    {
        public UploadBatch {|#0:MyBatch|} { get; set; }
    }
}";

            var expected = VerifyCS.Diagnostic("RE0001")
                .WithLocation(0)
                .WithArguments("UploadBatch", "a property");
                
            await VerifyCS.VerifyAnalyzerAsync(testCode, expected);
        }

        [TestMethod]
        public async Task LocalVariable_ShouldNotTriggerDiagnostic()
        {
            const string testCode = @"
using RockEngine.Vulkan;

namespace TestNamespace
{
    public class TestClass
    {
        public void ValidMethod()
        {
            var localBatch = new UploadBatch();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }
    }
}
