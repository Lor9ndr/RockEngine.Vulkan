using RockEngine.Vulkan.Assets;
using PropertyChanged;
using System.Text.Json.Serialization;

namespace RockEngine.Vulkan.ECS
{
    [AddINotifyPropertyChangedInterface]
    public class Project : IAsset, IDisposable
    {
        public Guid ID { get; private set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public List<Scene> Scenes { get; set; } = new List<Scene>();

        [JsonIgnore]
        public Scene CurrentScene;

        // This property will be automatically set to true when any other property changes
        [JsonIgnore]
        public bool IsChanged { get; set; } = false;

        /// <summary>
        /// Dynamically calculate the AssetPath based on the Path property
        /// </summary>
        public string AssetPath
        {
            get
            {
                // Extract the directory path from the full path
                string directoryPath = System.IO.Path.GetDirectoryName(Path)!;
                // Combine the directory path with the "Assets" folder
                return System.IO.Path.Combine(directoryPath, "Assets");
            }
        }

        [JsonConstructor]
        internal Project(Guid id, string name, string path, List<Scene> scenes)
        {
            ID = id;
            Name = name;
            Path = path;
            Scenes = scenes;
            TryCreateAssetFolder();
            IsChanged = false;
        }

        public Project(string name, string path)
        {
            ID = Guid.NewGuid();
            Name = name;
            Path = path;
            TryCreateAssetFolder();
            IsChanged = false;
            AddScene(new Scene("Scene 1", this));
            CurrentScene = Scenes[0];
        }

        private void TryCreateAssetFolder()
        {
            if (Directory.Exists(AssetPath))
            {
                return;
            }
            Directory.CreateDirectory(AssetPath);
        }

        public void AddScene(Scene scene) => Scenes.Add(scene);

        public void Dispose()
        {
            foreach (var item in Scenes)
            {
                item.Dispose();
            }
        }
    }
}