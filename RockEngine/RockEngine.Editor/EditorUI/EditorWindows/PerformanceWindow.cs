using ImGuiNET;

using RockEngine.Core;


namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public class PerformanceWindow : EditorWindow
    {
        public PerformanceWindow() : base("Performance") { }

        protected override void OnDraw()
        {
            PerformanceTracer.DrawMetrics();


        }

        private void DrawMemoryStats()
        {

        }
    }
}
