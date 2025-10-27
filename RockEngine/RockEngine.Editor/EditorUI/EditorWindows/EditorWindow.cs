using ImGuiNET;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public abstract class EditorWindow
    {
        protected bool _isOpen = true;

        public string Title { get; protected set; }
        public bool IsOpen { get => _isOpen; set => _isOpen = value; }
        public Vector2 Size { get; protected set; }
        public Vector2 Position { get; protected set; }

        protected EditorWindow(string title)
        {
            Title = title;
        }

        public virtual async ValueTask Draw()
        {
            if (!IsOpen)
            {
                return;
            }

            if (ImGui.Begin(Title, ref _isOpen))
            {
                await OnDraw();
            }
            ImGui.End();
        }

        protected abstract ValueTask OnDraw();

        protected virtual void ApplyWindowStyling()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        }

        protected virtual void PopWindowStyling()
        {
            ImGui.PopStyleVar();
        }
    }
}