using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{

    [Export(typeof(ITaggerProvider))]
    [ContentType(GlslContentTypeDefinitions.GlslContentType)]
    [TagType(typeof(IErrorTag))]
    public class GlslErrorTaggerProvider : ITaggerProvider
    {
        private static string GetValidatorPath()
        {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string packageDir = Path.GetDirectoryName(assemblyLocation);
            return Path.Combine(packageDir, "refs", "ShaderValidator.exe");
        }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            string validatorPath = GetValidatorPath();
            if (!File.Exists(validatorPath))
            {
                return null; // validator missing, no error tagging
            }

            return buffer.Properties.GetOrCreateSingletonProperty(() => new GlslErrorTagger(buffer, validatorPath)) as ITagger<T>;
        }
    }
}