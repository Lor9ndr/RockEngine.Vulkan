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
        // Show built‑in variables only when in a context where a variable is expected
        if (!IsVariableContext(snapshot, triggerPoint))
            yield break;

        foreach (var varName in GlslBuiltIns.BuiltInVariables)
        {
            yield return new Completion(varName, varName, "GLSL built‑in variable", null, null);
        }
    }

    private bool IsVariableContext(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
    {
        // Reuse the same logic as in VariableCompletionContributor (or extract to a helper)
        return VariableCompletionContributor.IsVariableContext(snapshot, triggerPoint);
    }
}