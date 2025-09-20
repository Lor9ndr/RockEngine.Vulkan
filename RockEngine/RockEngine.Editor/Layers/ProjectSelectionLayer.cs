using ImGuiNET;

using NLog;

using RockEngine.Core;
using RockEngine.Core.Rendering;
using RockEngine.Editor.EditorUI;
using RockEngine.Vulkan;

using Silk.NET.Windowing;

using System.Numerics;

namespace RockEngine.Editor.Layers
{
    public class ProjectSelectionLayer : ILayer
    {
        private readonly ProjectSelectionManager _projectManager;
        private readonly IWindow _window;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private bool _showCreateProjectModal = false;
        private string _newProjectName = "New Project";
        private string _newProjectPath = "";
        private string _statusMessage = "";
        private bool _statusError = false;
        private bool _isOperationInProgress = false;
        private string _operationInProgress = "";

        public ProjectSelectionLayer(ProjectSelectionManager projectManager, IWindow window)
        {
            _projectManager = projectManager;
            _window = window;
        }

        public Task OnAttach()
        {
            _logger.Info("Project selection layer attached");
            return Task.CompletedTask;
        }

        public void OnDetach()
        {
            _logger.Info("Project selection layer detached");
        }

        public void OnUpdate()
        {
            // Check for completed async operations if needed
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
            // No rendering logic needed for this layer
        }

        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            // Show progress modal if an operation is in progress
            if (_isOperationInProgress)
            {
                Vector2 center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
                ImGui.SetNextWindowContentSize(new Vector2(200, 150));
                ImGui.OpenPopup("Operation in Progress");
                if (ImGui.BeginPopupModal("Operation in Progress", ref _isOperationInProgress, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
                {
                    ImGui.Text($"{_operationInProgress}...");
                    // Center the spinner using available space
                    float spinnerRadius = 20f;
                    float spinnerThickness = 4f;
                    uint spinnerColor = ImGui.GetColorU32(ImGuiCol.ButtonHovered);

                    // Calculate center position within the popup
                    Vector2 windowSize = ImGui.GetWindowSize();
                    Vector2 windowPos = ImGui.GetWindowPos();
                    Vector2 centerPos = new Vector2(
                        windowPos.X + windowSize.X * 0.5f,
                        windowPos.Y + windowSize.Y * 0.5f + ImGui.GetTextLineHeight() // Offset below text
                    );

                    // Draw the spinner at the calculated center position
                    ImGuiSpinnerExtension.Spinner(centerPos, spinnerRadius, spinnerThickness, spinnerColor);

                    ImGui.EndPopup();
                }
            }

            // Center the project selection window
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 400), ImGuiCond.Always);

            if (ImGui.Begin("Project Selection", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove))
            {
                ImGui.Text("Select a project to open or create a new one:");

                // Recent projects list
                ImGui.Separator();
                ImGui.Text("Recent Projects:");

                if (_projectManager.RecentProjects.Count == 0)
                {
                    ImGui.TextDisabled("No recent projects found");
                }
                else
                {
                    foreach (var project in _projectManager.RecentProjects)
                    {
                        if (ImGui.Selectable($"{project.Name}##{project.Path}", false) && !_isOperationInProgress)
                        {
                            _ = OpenProjectAsync(project.Path);
                        }

                        // Show tooltip with full path
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(project.Path);
                        }
                    }
                }

                ImGui.Separator();

                // Browse for project button
                if (ImGui.Button("Browse...") && !_isOperationInProgress)
                {
                    var path = PlatformFileDialog.OpenFile("RockEngine Project|*.rockproj", "Open Project");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        _ = OpenProjectAsync(path);
                    }
                    else if (!string.IsNullOrEmpty(path))
                    {
                        // Show error message if file doesn't exist
                        _statusMessage = "File does not exist";
                        _statusError = true;
                    }
                }

                ImGui.SameLine();

                // Create new project button
                if (ImGui.Button("Create New Project") && !_isOperationInProgress)
                {
                    _showCreateProjectModal = true;
                    _newProjectName = "New Project";
                    _newProjectPath = "";
                    _statusMessage = "";
                }

                // Show status message if any
                if (!string.IsNullOrEmpty(_statusMessage))
                {
                    if (_statusError)
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(1, 0, 0, 1), _statusMessage);
                    }
                    else
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), _statusMessage);
                    }
                }

                ImGui.End();
            }

            // Create project modal
            if (_showCreateProjectModal && !_isOperationInProgress)
            {
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(500, 300), ImGuiCond.Always);
                if (ImGui.Begin("Create New Project", ref _showCreateProjectModal, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
                {
                    ImGui.InputText("Project Name", ref _newProjectName, 100);

                    ImGui.Text("Project Location:");
                    ImGui.Text(_newProjectPath);

                    if (ImGui.Button("Browse..."))
                    {
                        var path = PlatformFileDialog.OpenFolder("Select Project Location");
                        if (!string.IsNullOrEmpty(path))
                        {
                            _newProjectPath = path;
                        }
                    }

                    ImGui.Separator();

                    if (ImGui.Button("Create"))
                    {
                        if (string.IsNullOrEmpty(_newProjectName))
                        {
                            _statusMessage = "Project name cannot be empty";
                            _statusError = true;
                        }
                        else if (string.IsNullOrEmpty(_newProjectPath))
                        {
                            _statusMessage = "Please select a project location";
                            _statusError = true;
                        }
                        else
                        {
                            var fullPath = Path.Combine(_newProjectPath, _newProjectName + ".rockproj");
                            _ = CreateProjectAsync(_newProjectName, fullPath);
                        }
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Cancel"))
                    {
                        _showCreateProjectModal = false;
                    }

                    ImGui.End();
                }
            }
        }

        private async Task OpenProjectAsync(string path)
        {
            _isOperationInProgress = true;
            _operationInProgress = "Opening project";

            try
            {
                var success = await _projectManager.OpenProjectAsync(path,this);
                if (!success)
                {
                    _statusMessage = "Failed to open project";
                    _statusError = true;
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error opening project: {ex.Message}";
                _statusError = true;
                _logger.Error(ex, "Error opening project");
            }
            finally
            {
                _isOperationInProgress = false;
                _operationInProgress = "";
            }
        }

        private async Task CreateProjectAsync(string name, string path)
        {
            _isOperationInProgress = true;
            _operationInProgress = "Creating project";

            try
            {
                var success = await _projectManager.CreateProjectAsync(name, path);
                if (success)
                {
                    _statusMessage = "Project created successfully";
                    _statusError = false;
                    _showCreateProjectModal = false;
                    await OpenProjectAsync(path);
                }
                else
                {
                    _statusMessage = "Failed to create project";
                    _statusError = true;
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error creating project: {ex.Message}";
                _statusError = true;
                _logger.Error(ex, "Error creating project");
            }
            finally
            {
                _isOperationInProgress = false;
                _operationInProgress = "";
            }
        }
    }
}