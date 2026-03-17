using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = GlslClassificationTypes.GlslBuiltInVariable)]
    [Name(GlslClassificationTypes.GlslBuiltInVariable)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class GlslBuiltInVariableFormat : ClassificationFormatDefinition
    {
        public GlslBuiltInVariableFormat()
        {
            DisplayName = "GLSL Built‑in Variable";
            ForegroundColor = Colors.DarkOrange; // choose a distinct color
        }
    }
}