using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = GlslClassificationTypes.GlslType)]
    [Name(GlslClassificationTypes.GlslType)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class GlslTypeFormat : ClassificationFormatDefinition
    {
        public GlslTypeFormat()
        {
            DisplayName = "GLSL Type";
            ForegroundColor = System.Windows.Media.Colors.Teal;
        }
    }
}