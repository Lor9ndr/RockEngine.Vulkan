using ImGuiNET;

using System.Diagnostics;
using System.Numerics;

namespace RockEngine.Core
{
    public static class PerformanceTracer
    {
        private static readonly Dictionary<string, Queue<double>> _durations = new();
        private static readonly Dictionary<string, Stopwatch> _activeTimers = new();
        private const int MAX_HISTORY = 100;

        public static IDisposable BeginSection(string name)
        {
            var sw = Stopwatch.StartNew();
            return new SectionTracker(name, sw);
        }

        public static double GetAverageDuration(string name)
        {
            lock (_durations)
            {
                if (_durations.TryGetValue(name, out var values) && values.Count != 0)
                    return values.Average();
                return 0;
            }
        }

        public static void DrawMetrics()
        {
            if (ImGui.Begin("Performance Metrics"))
            {
                lock (_durations)
                {
                    foreach (var (name, values) in _durations)
                    {
                        var avg = values.Count != 0 ? values.Average() : 0;
                        ImGui.TextWrapped(name);
                        ImGui.ProgressBar((float)(avg / 16.67), new Vector2(200, 20),
                            $"{avg:F2}ms");
                    }
                }
                ImGui.End();
            }
        }

        private class SectionTracker : IDisposable
        {
            private readonly string _name;
            private readonly Stopwatch _sw;

            public SectionTracker(string name, Stopwatch sw)
            {
                _name = name;
                _sw = sw;
                _activeTimers[_name] = sw;
            }

            public void Dispose()
            {
                _sw.Stop();
                lock (_durations)
                {
                    if (!_durations.ContainsKey(_name))
                        _durations[_name] = new Queue<double>(MAX_HISTORY);

                    var queue = _durations[_name];
                    if (queue.Count >= MAX_HISTORY) queue.Dequeue();
                    queue.Enqueue(_sw.Elapsed.TotalMilliseconds);
                }
                _activeTimers.Remove(_name);
            }
        }
    }
}
