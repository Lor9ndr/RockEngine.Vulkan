using System.Collections.Generic;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal interface IGlslCompletionContributor
    {
        /// <summary>
        /// Returns a list of completions to be shown in the IntelliSense session.
        /// </summary>
        /// <param name="snapshot">Current text snapshot.</param>
        /// <param name="triggerPoint">Position where completion was triggered.</param>
        IEnumerable<Completion> GetCompletions(ITextSnapshot snapshot, SnapshotPoint triggerPoint);
    }

}
