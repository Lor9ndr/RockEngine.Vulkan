using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal class GlslCompletionSource : ICompletionSource
    {
        private readonly ITextBuffer _buffer;
        private readonly IEnumerable<IGlslCompletionContributor> _contributors;
        private bool _disposed;

        public GlslCompletionSource(ITextBuffer buffer, IEnumerable<IGlslCompletionContributor> contributors)
        {
            _buffer = buffer;
            _contributors = contributors;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            if (_disposed) return;

            var triggerPoint = session.GetTriggerPoint(_buffer.CurrentSnapshot);
            if (!triggerPoint.HasValue) return;

            var snapshot = _buffer.CurrentSnapshot;
            var line = snapshot.GetLineFromPosition(triggerPoint.Value.Position);
            var lineText = line.GetText();
            int posInLine = triggerPoint.Value.Position - line.Start.Position;

            int start = posInLine;
            while (start > 0 && (char.IsLetterOrDigit(lineText[start - 1]) || lineText[start - 1] == '_'))
                start--;
            int end = posInLine;
            while (end < lineText.Length && (char.IsLetterOrDigit(lineText[end]) || lineText[end] == '_'))
                end++;

            var applicableTo = snapshot.CreateTrackingSpan(
                line.Start + start,
                end - start,
                SpanTrackingMode.EdgeInclusive
            );

            var completions = new List<Completion>();

            foreach (var contributor in _contributors)
            {
                try
                {
                    completions.AddRange(contributor.GetCompletions(snapshot, triggerPoint.Value));
                }
                catch (Exception ex)
                {
                    Debug.Fail($"Error in completion contributor {contributor.GetType().Name}: {ex}");
                }
            }

            var completionSet = new CompletionSet(
                "GLSL", "GLSL",
                applicableTo,
                completions,
                Enumerable.Empty<Completion>()
            );
            completionSets.Add(completionSet);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}