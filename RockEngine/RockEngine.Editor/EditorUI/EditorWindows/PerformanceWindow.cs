using ImGuiNET;

using RockEngine.Core;


namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class PerformanceWindow : EditorWindow
    {
        public PerformanceWindow() : base("Performance") { }

        protected override ValueTask OnDraw()
        {
            PerformanceTracer.DrawMetrics();
            return ValueTask.CompletedTask;
        }

        private void DrawMemoryStats()
        {

        }
    }
}
