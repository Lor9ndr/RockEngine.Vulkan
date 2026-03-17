using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(GlslContentTypeDefinitions.GlslContentType)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Name("GLSL Completion Handler")]
    internal class GlslCompletionHandlerProvider : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService { get; set; }

        [Import]
        internal ICompletionBroker CompletionBroker { get; set; }

        [Import]
        internal SVsServiceProvider ServiceProvider { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            var textView = AdapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) return;

            textView.Properties.GetOrCreateSingletonProperty(
                () => new GlslCompletionCommandHandler(textViewAdapter, textView, this));
        }
    }
}