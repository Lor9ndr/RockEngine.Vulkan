using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [ContentType(GlslContentTypeDefinitions.GlslContentType)]
    [Name("GLSL Quick Info")]
    public class GlslQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(() => new GlslAsyncQuickInfoSource(textBuffer));
        }
    }
}