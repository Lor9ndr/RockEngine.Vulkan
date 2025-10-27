using ImGuiNET;

using RockEngine.Core;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public class Toolbar
    {
        private readonly EditorStateManager _stateManager;

        // Icons
        private const string ICON_PLAY = "\uf04b";
        private const string ICON_PAUSE = "\uf04c";
        private const string ICON_STOP = "\uf04d";
        private const string ICON_STEP = "\uf051";

        public Toolbar()
        {
            _stateManager = new EditorStateManager();
        }

        public void Draw()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 4));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 0));

            if (ImGui.Begin("Toolbar", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoTitleBar))
            {
                var buttonSize = new Vector2(32, 32);
                var style = ImGui.GetStyle();
                var colors = style.Colors;

                // Play/Pause/Stop controls
                var state = _stateManager.State;
                var isPlaying = state == EditorState.Play;
                var isPaused = state == EditorState.Paused;

                // Play Button
                ImGui.PushStyleColor(ImGuiCol.Button, isPlaying ? colors[(int)ImGuiCol.ButtonActive] : colors[(int)ImGuiCol.Button]);
                if (ImGui.Button($"{ICON_PLAY}##Play", buttonSize))
                {
                    _stateManager.SetState(EditorState.Play);
                }

                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Play");
                }

                ImGui.SameLine();

                // Pause Button
                ImGui.BeginDisabled(!isPlaying && !isPaused);
                ImGui.PushStyleColor(ImGuiCol.Button, isPaused ? colors[(int)ImGuiCol.ButtonActive] : colors[(int)ImGuiCol.Button]);
                if (ImGui.Button($"{ICON_PAUSE}##Pause", buttonSize))
                {
                    _stateManager.SetState(isPaused ? EditorState.Play : EditorState.Paused);
                }

                ImGui.PopStyleColor();
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(isPaused ? "Resume" : "Pause");
                }

                ImGui.SameLine();

                // Stop Button
                ImGui.BeginDisabled(state == EditorState.Edit);
                if (ImGui.Button($"{ICON_STOP}##Stop", buttonSize))
                {
                    _stateManager.SetState(EditorState.Edit);
                }

                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Stop");
                }

                ImGui.SameLine();

                // Step Button
                ImGui.BeginDisabled(!isPaused);
                if (ImGui.Button($"{ICON_STEP}##Step", buttonSize))
                {
                    // Step frame logic
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Step Forward");
                }

                ImGui.SameLine();
                ImGui.Separator();
                ImGui.SameLine();

                // Editor state display
                var stateText = state.ToString();
                var stateColor = state switch
                {
                    EditorState.Play => new Vector4(0.0f, 0.8f, 0.0f, 1.0f),
                    EditorState.Paused => new Vector4(0.8f, 0.8f, 0.0f, 1.0f),
                    _ => new Vector4(0.6f, 0.6f, 0.6f, 1.0f)
                };

                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8);
                ImGui.TextColored(stateColor, stateText);

                // FPS counter on the right
                ImGui.SameLine(ImGui.GetWindowWidth() - 80);
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $"{Time.FPS} FPS");
            }

            ImGui.End();
            ImGui.PopStyleVar(2);
        }
    }
}