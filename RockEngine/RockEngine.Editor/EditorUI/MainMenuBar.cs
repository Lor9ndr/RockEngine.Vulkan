using ImGuiNET;

namespace RockEngine.Editor.EditorUI
{
    public class MainMenuBar
    {
        public event Action<string, bool> ViewToggled;

        // Window visibility states
        private bool _showSceneHierarchy = true;
        private bool _showInspector = true;
        private bool _showMaterialTemplates = true;
        private bool _showPerformanceMetrics = true;
        private bool _showMemoryStats = true;
        private bool _showConsole = true;

        // Icons
        private const string ICON_EYE = "\uf06e";
        private const string ICON_INFO = "\uf129";
        private const string ICON_MATERIAL = "\uf53f";
        private const string ICON_CHART = "\uf080";
        private const string ICON_MEMORY = "\uf233";
        private const string ICON_LIST = "\uf03a";

        public void Draw()
        {
            if (ImGui.BeginMainMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("New Scene")) { }
                    if (ImGui.MenuItem("Open Scene")) { }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Save Scene")) { }
                    if (ImGui.MenuItem("Save Scene As...")) { }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Exit")) { }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Edit"))
                {
                    if (ImGui.MenuItem("Undo")) { }
                    if (ImGui.MenuItem("Redo")) { }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Cut")) { }
                    if (ImGui.MenuItem("Copy")) { }
                    if (ImGui.MenuItem("Paste")) { }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("View"))
                {
                    if (ImGui.MenuItem($"{ICON_EYE} Scene Hierarchy", null, ref _showSceneHierarchy))
                    {
                        ViewToggled?.Invoke("Scene Hierarchy", _showSceneHierarchy);
                    }

                    if (ImGui.MenuItem($"{ICON_INFO} Inspector", null, ref _showInspector))
                    {
                        ViewToggled?.Invoke("Inspector", _showInspector);
                    }

                    if (ImGui.MenuItem($"{ICON_MATERIAL} Material Templates", null, ref _showMaterialTemplates))
                    {
                        ViewToggled?.Invoke("Material Templates", _showMaterialTemplates);
                    }

                    if (ImGui.MenuItem($"{ICON_CHART} Performance", null, ref _showPerformanceMetrics))
                    {
                        ViewToggled?.Invoke("Performance", _showPerformanceMetrics);
                    }

                    if (ImGui.MenuItem($"{ICON_MEMORY} Memory Stats", null, ref _showMemoryStats))
                    {
                        ViewToggled?.Invoke("Memory Stats", _showMemoryStats);
                    }

                    if (ImGui.MenuItem($"{ICON_LIST} Console", null, ref _showConsole))
                    {
                        ViewToggled?.Invoke("Console", _showConsole);
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Assets"))
                {
                    if (ImGui.MenuItem("Create Material")) { }
                    if (ImGui.MenuItem("Import Model")) { }
                    if (ImGui.MenuItem("Import Texture")) { }
                    ImGui.EndMenu();
                }

                ImGui.EndMainMenuBar();
            }
        }
    }
}