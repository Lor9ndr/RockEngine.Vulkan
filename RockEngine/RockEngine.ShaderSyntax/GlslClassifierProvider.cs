using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(IClassifierProvider))]
    [ContentType(GlslContentTypeDefinitions.GlslContentType)]
    public class GlslClassifierProvider : IClassifierProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry = null;

        public IClassifier GetClassifier(ITextBuffer buffer)
        {
            return buffer.Properties.GetOrCreateSingletonProperty(() => new GlslClassifier(ClassificationRegistry));
        }
    }
}