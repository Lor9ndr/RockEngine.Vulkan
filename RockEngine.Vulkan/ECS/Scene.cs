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
                await entity.InitalizeAsync(context);
            }
        }
        public async Task IntializeAsync(VulkanContext context)
        {
            var tsks = new Task[_entities.Count];
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity? item = _entities[i];
                tsks[i] = item.InitalizeAsync(context);
            }
            await Task.WhenAll(tsks);
        }

        public void Render(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            foreach (var item in _entities)
            {
                item.Render(context, commandBuffer);
            }
        }

        internal async Task Update(double time, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            foreach (var entity in _entities)
            {
                await entity.Update(time, context, commandBuffer);
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
