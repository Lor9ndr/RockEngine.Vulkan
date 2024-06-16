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

        public async Task AddEntity(VulkanContext context, Entity entity)
        {
            _entities.Add(entity);
            if (_isInitalized)
            {
                await entity.InitializeAsync(context);
            }
        }

        public async Task InitializeAsync(VulkanContext context)
        {
            foreach (var item in _entities)
            {
                await item.InitializeAsync(context);
            }
            _isInitalized = true;
        }
        

        public async Task RenderAsync(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            foreach (var item in _entities)
            {
                await item.RenderAsync(context, commandBuffer);
            }
        }

        internal void Update(double time)
        {
            foreach (var item in _entities)
            {
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
