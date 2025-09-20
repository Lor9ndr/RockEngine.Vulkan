using NLog;
using NLog.Targets;

using System;

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

            // Store original colors
            var originalForeground = Console.ForegroundColor;
            var originalBackground = Console.BackgroundColor;

            // Set colors based on log level
            if (logEvent.Level == LogLevel.Trace)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
            }
            else if (logEvent.Level == LogLevel.Debug)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
            }
            else if (logEvent.Level == LogLevel.Info)
            {
                Console.ForegroundColor = ConsoleColor.White;
            }
            else if (logEvent.Level == LogLevel.Warn)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            else if (logEvent.Level == LogLevel.Error)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            else if (logEvent.Level == LogLevel.Fatal)
            {
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
            }

            // Write the message
            Console.WriteLine(logEvent.FormattedMessage);

            // Reset to original colors
            Console.ForegroundColor = originalForeground;
            Console.BackgroundColor = originalBackground;
        }
    }
}