using ImGuiNET;

using NLog;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.Logging
{
    public sealed class EditorConsole
    {
        private const int MAX_MESSAGES = 1000;
        private readonly List<LogEntry> _logEntries = new List<LogEntry>();
        private readonly Lock _lock = new Lock();
        private bool _autoScroll = true;
        private bool _showInfo = true;
        private bool _showWarn = true;
        private bool _showError = true;
        private bool _showTrace = true;
        private string _filter = string.Empty;

        public void AddLog(LogLevel level, string message)
        {
            var entry = new LogEntry
            {
                Level = level,
                Message = message,
                Time = DateTime.Now
            };

            lock (_lock)
            {
                _logEntries.Add(entry);
                if (_logEntries.Count > MAX_MESSAGES)
                {
                    _logEntries.RemoveAt(0);
                }
            }
        }

        public void Draw()
        {
            if (ImGui.Begin("Console"))
            {
                // Filter options
                ImGui.Checkbox("Trace", ref _showTrace);
                ImGui.SameLine();
                ImGui.Checkbox("Info", ref _showInfo);
                ImGui.SameLine();
                ImGui.Checkbox("Warn", ref _showWarn);
                ImGui.SameLine();
                ImGui.Checkbox("Error", ref _showError);
                ImGui.SameLine();
                if (ImGui.Button("Clear"))
                {
                    lock (_lock)
                    {
                        _logEntries.Clear();
                    }
                }
                ImGui.SameLine();
                ImGui.Checkbox("Auto-scroll", ref _autoScroll);
                ImGui.SameLine();
                ImGui.InputText("Filter", ref _filter, 100);

                ImGui.Separator();

                // Display logs
                ImGui.BeginChild("ScrollingRegion", new Vector2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.None,  ImGuiWindowFlags.HorizontalScrollbar);

                List<LogEntry> entries;
                lock (_lock)
                {
                    entries = new List<LogEntry>(_logEntries);
                }

                foreach (var entry in entries)
                {
                    if (!ShouldShow(entry)) continue;

                    // Color based on log level
                    Vector4 color = GetColorForLogLevel(entry.Level);
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.TextUnformatted($"[{entry.Time:HH:mm:ss}] {entry.Message}");
                    ImGui.PopStyleColor();
                }

                // Auto-scroll to bottom
                if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                {
                    ImGui.SetScrollHereY(1.0f);
                }

                ImGui.EndChild();
                ImGui.End();
            }
        }

        private bool ShouldShow(LogEntry entry)
        {
            if (!_showTrace && entry.Level == LogLevel.Trace) return false;
            if (!_showInfo && entry.Level == LogLevel.Info) return false;
            if (!_showWarn && entry.Level == LogLevel.Warn) return false;
            if (!_showError && entry.Level == LogLevel.Error) return false;

            if (!string.IsNullOrEmpty(_filter) &&
                !entry.Message.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private Vector4 GetColorForLogLevel(LogLevel level)
        {
            if (level == LogLevel.Error) return new Vector4(1.0f, 0.0f, 0.0f, 1.0f); // Red
            if (level == LogLevel.Warn) return new Vector4(1.0f, 1.0f, 0.0f, 1.0f);  // Yellow
            if (level == LogLevel.Info) return new Vector4(0.0f, 1.0f, 0.0f, 1.0f);  // Green
            if (level == LogLevel.Trace) return new Vector4(0.5f, 0.5f, 0.5f, 1.0f); // Gray
            return new Vector4(1.0f, 1.0f, 1.0f, 1.0f); // White
        }

        private class LogEntry
        {
            public LogLevel Level { get; set; }
            public string Message { get; set; }
            public DateTime Time { get; set; }
        }
    }
}
