using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = GlslClassificationTypes.GlslUserFunction)]
    [Name(GlslClassificationTypes.GlslUserFunction)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class GlslUserFunctionFormat : ClassificationFormatDefinition
    {
        public GlslUserFunctionFormat()
        {
            DisplayName = "GLSL User Function";
            ForegroundColor = Colors.DodgerBlue; // choose a distinct color
        }
    }
}

