using Microsoft.CodeAnalysis.CodeRefactorings;

namespace RockEngine.Analyzer.Test
{
    public static partial class CSharpCodeRefactoringVerifier<TCodeRefactoring>
        where TCodeRefactoring : CodeRefactoringProvider, new()
    {
        public class Test : CSharpCodeRefactoringTest<TCodeRefactoring, DefaultVerifier>
        {
            public Test()
            {
                SolutionTransforms.Add((solution, projectId) =>
                {
                    var compilationOptions = solution.GetProject(projectId).CompilationOptions;
                    compilationOptions = compilationOptions.WithSpecificDiagnosticOptions(
                        compilationOptions.SpecificDiagnosticOptions.SetItems(CSharpVerifierHelper.NullableWarnings));
                    solution = solution.WithProjectCompilationOptions(projectId, compilationOptions);

                    return solution;
                });
            }
        }
    }
}
