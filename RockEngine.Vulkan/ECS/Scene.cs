using PropertyChanged;

using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.VkObjects;

using System.Text.Json.Serialization;

namespace RockEngine.Vulkan.ECS
{
    [AddINotifyPropertyChangedInterface]
    public class Scene : IAsset, IDisposable
    {
        [JsonRequired, JsonInclude, JsonPropertyName("Entities")]
        private List<Entity> _entities = new List<Entity>();
        
        [JsonInclude]
        public Guid ID { get; set;}

        public string Name { get;set;}
        public string Path { get;set; }
        [JsonIgnore]
        public bool IsChanged { get;set;}

        private bool _isInitalized = false;

        [JsonConstructor]
        internal Scene(Guid id, string name, string path, List<Entity> entities)
        {
            ID = id;
            Name = name;
            Path = path;
            _entities = entities;
        }

        public Scene(string name, Project project)
        {
            ID = Guid.NewGuid();
            Name = name;
            Path = project.AssetPath + "\\" + Name + IAsset.FILE_EXTENSION;
        }

        public async Task AddEntityAsync(Entity entity)
        {
            _entities.Add(entity);
            if (_isInitalized)
            {
                await entity.InitializeAsync();
            }
        }

        public async Task InitializeAsync()
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                 await _entities[i].InitializeAsync();
            }
            _isInitalized = true;
        }

        public IEnumerable<Entity> GetEntities()
        {
            return _entities;
        }

        internal void Update(double time)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity? item = _entities[i];
                item.Update(time);
            }
        }

        public void Dispose()
        {
            foreach (var item in _entities)
            {
                item.Dispose();
            }
        }
    }
}
