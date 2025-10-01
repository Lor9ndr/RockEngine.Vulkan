using Newtonsoft.Json;

using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;

namespace RockEngine.Editor.EditorUI
{
    public class ProjectSelectionManager
    {
        private readonly AssetManager _assetManager;
        private List<ProjectInfo> _recentProjects = new List<ProjectInfo>();
        private const string RecentProjectsFile = "recent_projects.json";
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public IReadOnlyList<ProjectInfo> RecentProjects => _recentProjects.AsReadOnly();

        public ProjectSelectionManager(AssetManager assetManager)
        {
            _assetManager = assetManager;
            LoadRecentProjects();
        }

        public async Task<bool> OpenProjectAsync(string path, params ILayer[] excludeLayers)
        {
            try
            {
                if (!File.Exists(path))
                {
                    _logger.Error($"Project file not found: {path}");
                    return false;
                }

                // Load project using AssetManager
                var project = await _assetManager.LoadProjectAsync(path);

                // Add to recent projects
                AddToRecentProjects(new ProjectInfo
                {
                    Name = project.Name,
                    Path = path,
                    LastOpened = DateTime.Now
                });

                // Initialize the engine with this project
                await InitializeEngineWithProjectAsync(project, excludeLayers);

                _logger.Info($"Project opened: {project.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error opening project: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CreateProjectAsync(string name, string path)
        {
            try
            {
                // Create project using AssetManager
                var project = await _assetManager.CreateProjectAsync(name, Path.GetDirectoryName(path));

                // Add to recent projects
                AddToRecentProjects(new ProjectInfo
                {
                    Name = name,
                    Path = path,
                    LastOpened = DateTime.Now
                });

                _logger.Info($"Project created: {name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error creating project: {ex.Message}");
                return false;
            }
        }

        private async Task InitializeEngineWithProjectAsync(ProjectAsset project, params ILayer[] excludeLayers)
        {
            // Set current directory to project directory
            //Directory.SetCurrentDirectory(Path.GetDirectoryName(project.Path.ToString()));
            var layerStack = IoC.Container.GetInstance<LayerStack>();
            var allLayers = IoC.Container.GetAllInstances<ILayer>();
            
            foreach (var item in excludeLayers)
            {
                layerStack.PopLayer(item);
            }

            foreach (var layer in allLayers.Except(excludeLayers))
            {
                await layerStack.PushLayer(layer);
            }
        }

        private void LoadRecentProjects()
        {
            try
            {
                if (File.Exists(RecentProjectsFile))
                {
                    var json = File.ReadAllText(RecentProjectsFile);
                    _recentProjects = JsonConvert.DeserializeObject<List<ProjectInfo>>(json) ?? new List<ProjectInfo>();

                    // Sort by last opened date (newest first)
                    _recentProjects = [.. _recentProjects
                        .OrderByDescending(p => p.LastOpened)
                        .Take(10)];
                }
            }
            catch
            {
                _recentProjects = new List<ProjectInfo>();
            }
        }

        private void SaveRecentProjects()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_recentProjects, Formatting.Indented);
                File.WriteAllText(RecentProjectsFile, json);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error saving recent projects: {ex.Message}");
            }
        }

        private void AddToRecentProjects(ProjectInfo projectInfo)
        {
            // Remove if already exists
            _recentProjects.RemoveAll(p => p.Path == projectInfo.Path);

            // Add to beginning of list
            _recentProjects.Insert(0, projectInfo);

            // Keep only 10 most recent
            if (_recentProjects.Count > 10)
            {
                _recentProjects = _recentProjects.Take(10).ToList();
            }

            SaveRecentProjects();
        }
        public void RemoveRecentProject(ProjectInfo projectInfo)
        {

        }
    }

    public class ProjectInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime LastOpened { get; set; }
    }
}