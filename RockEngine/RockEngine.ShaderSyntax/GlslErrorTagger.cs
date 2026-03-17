using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Threading;

namespace RockEngine.ShaderSyntax
{
    internal class GlslErrorTagger : ITagger<IErrorTag>
    {
        private readonly ITextBuffer _buffer;
        private readonly string _validatorPath;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _validationTask;
        private readonly object _lock = new object();
        private readonly List<ErrorData> _errors = new List<ErrorData>();

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        private class ErrorData
        {
            public ITrackingSpan TrackingSpan { get; set; }
            public string Level { get; set; }      // "error" or "warning"
            public string Message { get; set; }
        }

        // Matches JSON from ShaderValidator.exe
        private class ValidationMessage
        {
            public string File { get; set; }
            public int Line { get; set; }
            public int Column { get; set; }
            public string Level { get; set; }
            public string Message { get; set; }
        }

        public GlslErrorTagger(ITextBuffer buffer, string validatorPath)
        {
            _buffer = buffer;
            _validatorPath = validatorPath;
            _buffer.Changed += OnBufferChanged;
            TriggerValidation();
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // Debounce: wait 500ms after last change before validating
            TriggerValidation(500);
        }

        private void TriggerValidation(int delayMs = 0)
        {
            lock (_lock)
            {
                _cts.Cancel();
                var cts = new CancellationTokenSource();
                _validationTask = Task.Delay(delayMs, cts.Token)
                    .ContinueWith(async _ => await ValidateAsync(cts.Token), TaskScheduler.Default)
                    .Unwrap();
            }
        }

        private async Task ValidateAsync(CancellationToken cancellationToken)
        {
            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            string text = snapshot.GetText();

            // Get original file path if available
            string originalFilePath = null;
            if (_buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc))
            {
                originalFilePath = doc.FilePath;
            }

            string tempFile = Path.GetTempFileName() + GetFileExtension();
            File.WriteAllText(tempFile, text);

            try
            {
                string defines = "";
                var args = $"{tempFile} --compiler glslang";
                if (!string.IsNullOrEmpty(originalFilePath))
                    args += $" --original-file {originalFilePath}";
                if (!string.IsNullOrEmpty(defines))
                    args += $" --defines \"{defines}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _validatorPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return;

                var messages = JsonSerializer.Deserialize<ValidationMessage[]>(output) ?? Array.Empty<ValidationMessage>();

                var newErrors = new List<ErrorData>();

                foreach (var msg in messages)
                {
                    if (msg.Line < 0 || msg.Line >= snapshot.LineCount)
                        continue;

                    var line = snapshot.GetLineFromLineNumber(msg.Line);
                    int start = msg.Column >= 0 ? line.Start.Position + msg.Column : line.Start.Position;
                    int length = msg.Column >= 0 ? Math.Min(10, line.Length - msg.Column) : line.Length;
                    if (start + length > snapshot.Length)
                        length = snapshot.Length - start;

                    var span = new SnapshotSpan(snapshot, start, length);
                    var trackingSpan = snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive);

                    newErrors.Add(new ErrorData
                    {
                        TrackingSpan = trackingSpan,
                        Level = msg.Level,
                        Message = msg.Message
                    });
                }

                lock (_lock)
                {
                    _errors.Clear();
                    _errors.AddRange(newErrors);
                }
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Validation error: {ex}");
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        private string GetFileExtension()
        {
            if (_buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc))
                return Path.GetExtension(doc.FilePath);
            return ".vert";
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;

            ITextSnapshot currentSnapshot = spans[0].Snapshot;

            lock (_lock)
            {
                foreach (var error in _errors)
                {
                    SnapshotSpan currentSpan = error.TrackingSpan.GetSpan(currentSnapshot);
                    if (spans.IntersectsWith(currentSpan))
                    {
                        var tag = new ErrorTag(
                            error.Level == "error" ? PredefinedErrorTypeNames.SyntaxError : PredefinedErrorTypeNames.Warning,
                            error.Message);
                        yield return new TagSpan<IErrorTag>(currentSpan, tag);
                    }
                }
            }
        }
    }
}