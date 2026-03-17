using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType(GlslContentTypeDefinitions.GlslContentType)]
    [Name("GLSL Completion")]
    public class GlslCompletionSourceProvider : ICompletionSourceProvider
    {
        [ImportMany]
        internal IEnumerable<IGlslCompletionContributor> Contributors { get; set; }

        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(
                () => new GlslCompletionSource(textBuffer, Contributors.ToList())
            );
        }
    }
}