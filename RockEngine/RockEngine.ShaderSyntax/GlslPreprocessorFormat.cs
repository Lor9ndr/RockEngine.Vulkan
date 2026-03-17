using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = GlslClassificationTypes.GlslPreprocessor)]
    [Name(GlslClassificationTypes.GlslPreprocessor)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class GlslPreprocessorFormat : ClassificationFormatDefinition
    {
        public GlslPreprocessorFormat()
        {
            DisplayName = "GLSL Preprocessor";
            ForegroundColor = Colors.Gray;
        }
    }
}