using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = GlslClassificationTypes.GlslKeyword)]
    [Name(GlslClassificationTypes.GlslKeyword)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class GlslKeywordFormat : ClassificationFormatDefinition
    {
        public GlslKeywordFormat()
        {
            DisplayName = "GLSL Keyword";
            ForegroundColor = Colors.CadetBlue;
        }
    }
}