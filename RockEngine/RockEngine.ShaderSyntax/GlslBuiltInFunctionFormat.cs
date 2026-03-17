using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = GlslClassificationTypes.GlslBuiltInFunction)]
    [Name(GlslClassificationTypes.GlslBuiltInFunction)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class GlslBuiltInFunctionFormat : ClassificationFormatDefinition
    {
        public GlslBuiltInFunctionFormat()
        {
            DisplayName = "GLSL Built‑in Function";
            ForegroundColor = Colors.MediumPurple; // distinct from other colors
        }
    }
}