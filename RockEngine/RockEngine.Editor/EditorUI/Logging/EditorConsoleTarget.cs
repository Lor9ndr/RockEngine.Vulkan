using NLog;
using NLog.Targets;

namespace RockEngine.Editor.EditorUI.Logging
{
    [Target("EditorConsole")]
    public sealed class EditorConsoleTarget : Target
    {
        private readonly EditorConsole _console;

        public EditorConsoleTarget(EditorConsole console)
        {
            _console = console;
        }

        protected override void Write(LogEventInfo logEvent)
        {
            _console.AddLog(logEvent.Level, logEvent.FormattedMessage);
        }
    }
}
