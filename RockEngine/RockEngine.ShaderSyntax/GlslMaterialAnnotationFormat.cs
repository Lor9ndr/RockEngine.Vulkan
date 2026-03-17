using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = GlslClassificationTypes.GlslMaterialAnnotation)]
    [Name(GlslClassificationTypes.GlslMaterialAnnotation)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class GlslMaterialAnnotationFormat : ClassificationFormatDefinition
    {
        public GlslMaterialAnnotationFormat()
        {
            DisplayName = "GLSL Material Annotation";
            ForegroundColor = Colors.Purple;
        }
    }
}