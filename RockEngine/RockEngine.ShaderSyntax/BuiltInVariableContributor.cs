using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using RockEngine.ShaderSyntax;

[Export(typeof(IGlslCompletionContributor))]
internal class BuiltInVariableContributor : IGlslCompletionContributor
{
    public IEnumerable<Completion> GetCompletions(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
    {
        // Determine context
        bool isDeclarationContext = VariableCompletionContributor.IsDeclarationContext(snapshot, triggerPoint);
        bool isExpressionContext = VariableCompletionContributor.IsExpressionContext(snapshot, triggerPoint);

        // Show built‑ins in both contexts (though in declaration context they may be less useful)
        if (!isDeclarationContext && !isExpressionContext)
        {
            yield break;
        }

        // Get shader stage from the text buffer
        var stage = ShaderStageHelper.GetStage(snapshot.TextBuffer);
        var builtIns = new HashSet<string>(GlslStageBuiltIns.CommonVariables);
        if (GlslStageBuiltIns.StageVariables.TryGetValue(stage, out var stageVars))
        {
            builtIns.UnionWith(stageVars);
        }

        foreach (var varName in builtIns)
        {
            yield return new Completion(varName, varName, "GLSL built‑in variable", null, null);
        }
    }
}
