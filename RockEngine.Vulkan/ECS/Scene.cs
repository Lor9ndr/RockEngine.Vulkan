using PropertyChanged;

using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Text.Json.Serialization;

namespace RockEngine.Vulkan.ECS
{
    [AddINotifyPropertyChangedInterface]
    public class Scene : IAsset, IDisposable
    {
        [JsonInclude]
        private readonly List<Entity> _entities = new List<Entity>();

        public Guid ID { get;}

        public string Name { get;set;}
        public string Path { get;set; }
        [JsonIgnore]
        public bool IsChanged { get;set;}

        private bool _isInitalized = false;

        public Scene(Guid id, string name, string path)
        {
            ID = id;
            Name = name;
            Path = path;
        }

        public Scene(string name, Project project)
        {
            ID = Guid.NewGuid();
            Name = name;
            Path = project.AssetPath + "\\" + Name + IAsset.FILE_EXTENSION;
        }

        public async Task AddEntity( Entity entity)
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
                Entity? item = _entities[i];
                await item.InitializeAsync();
            }
            _isInitalized = true;
        }
        

        public async Task RenderAsync(CommandBufferWrapper commandBuffer)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity? item = _entities[i];
                await item.RenderAsync(commandBuffer);
            }
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
