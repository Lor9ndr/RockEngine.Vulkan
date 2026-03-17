using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(ISignatureHelpSourceProvider))]
    [ContentType(GlslContentTypeDefinitions.GlslContentType)]
    [Name("GLSL Signature Help")]
    [Order(Before = "default")]
    public class GlslSignatureHelpSourceProvider : ISignatureHelpSourceProvider
    {
        [ImportMany]
        internal IEnumerable<ISignatureHelpContributor> Contributors { get; set; }

        public ISignatureHelpSource TryCreateSignatureHelpSource(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(
                () => new GlslSignatureHelpSource(textBuffer, Contributors));
        }
    }
}