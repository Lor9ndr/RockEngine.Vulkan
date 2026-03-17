using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;

namespace RockEngine.ShaderSyntax
{
    internal class GlslSignatureHelpCommandHandler : IOleCommandTarget
    {
        private readonly IOleCommandTarget _nextCommandHandler;
        private readonly ITextView _textView;
        private readonly ISignatureHelpBroker _broker;
        private readonly ITextStructureNavigator _navigator;
        private ISignatureHelpSession _session;

        internal GlslSignatureHelpCommandHandler(
            IVsTextView textViewAdapter,
            ITextView textView,
            ITextStructureNavigator navigator,
            ISignatureHelpBroker broker)
        {
            _textView = textView;
            _broker = broker;
            _navigator = navigator;
            textViewAdapter.AddCommandFilter(this, out _nextCommandHandler);
            Debug.WriteLine("GlslSignatureHelpCommandHandler created");
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            int hr = _nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR)
            {
                char typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                Debug.WriteLine($"Typed character: '{typedChar}'");

                if (typedChar == '(')
                {
                    Debug.WriteLine("Triggering signature help after insertion...");
                    _session = _broker.TriggerSignatureHelp(_textView);
                    if (_session == null)
                        Debug.WriteLine("TriggerSignatureHelp returned NULL");
                    else
                        Debug.WriteLine("Signature help session created");
                }
                else if (typedChar == ')' && _session != null)
                {
                    Debug.WriteLine("Dismissing session");
                    _session.Dismiss();
                    _session = null;
                }
            }
            return hr;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            return _nextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }
    }
}