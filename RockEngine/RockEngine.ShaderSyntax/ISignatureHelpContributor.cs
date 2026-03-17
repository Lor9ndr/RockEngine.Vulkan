using System.Collections.Generic;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

internal interface ISignatureHelpContributor
{
    IEnumerable<ISignature> GetSignatures(string functionName, ITextSnapshot snapshot, SnapshotPoint triggerPoint);
}