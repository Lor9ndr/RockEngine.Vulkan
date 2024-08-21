using PropertyChanged;
using RockEngine.Vulkan.Assets;
using RockEngine.Vulkan.Rendering;

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace RockEngine.Vulkan.ECS
{
    public class SceneNode
    {
        [JsonInclude]
        public Guid ID { get; set; }

        public string Name { get; set; }

        [JsonIgnore]
        public SceneNode Parent { get; private set; }

        private readonly List<SceneNode> _children = new List<SceneNode>();

        [JsonIgnore]
        public ReadOnlyCollection<SceneNode> Children => _children.AsReadOnly();

        public Entity Entity { get; set; }

        [JsonConstructor]
        public SceneNode(Guid id, string name, Entity entity)
        {
            ID = id;
            Name = name;
            Entity = entity;
        }

        public void AddChild(SceneNode child)
        {
            if (child.Parent != null)
            {
                child.Parent.RemoveChild(child);
            }
            child.Parent = this;
            _children.Add(child);
        }

        public void RemoveChild(SceneNode child)
        {
            if (_children.Remove(child))
            {
                child.Parent = null;
            }
        }

        public async Task InitializeAsync()
        {
            await Entity.InitializeAsync();
            foreach (var child in _children)
            {
                await child.InitializeAsync();
            }
        }

        public async ValueTask UpdateAsync(double time)
        {
            await Entity.UpdateAsync(time);
            foreach (var child in _children)
            {
                await child.UpdateAsync(time);
            }
        }

        public async Task RenderAsync(FrameInfo frameInfo)
        {
            await Entity.RenderAsync(frameInfo);
            foreach (var child in _children)
            {
                await child.RenderAsync(frameInfo);
            }
        }

        public void Dispose()
        {
            Entity.Dispose();
            foreach (var child in _children)
            {
                child.Dispose();
            }
        }
    }

    [AddINotifyPropertyChangedInterface]
    public class Scene : IAsset, IDisposable
    {
        private SceneNode _root;

        [JsonInclude]
        public Guid ID { get; set; }

        public string Name { get; set; }
        public string Path { get; set; }

        [JsonIgnore]
        public bool IsChanged { get; set; }

        private bool _isInitialized = false;

        [JsonConstructor]
        internal Scene(Guid id, string name, string path, SceneNode root)
        {
            ID = id;
            Name = name;
            Path = path;
            _root = root;
        }

        public Scene(string name, Project project)
        {
            ID = Guid.NewGuid();
            Name = name;
            Path = project.AssetPath + "\\" + Name + IAsset.FILE_EXTENSION;
            _root = new SceneNode(Guid.NewGuid(), "Root", new Entity());
        }

        public async Task AddEntityAsync(Entity entity, SceneNode parent = null)
        {
            var node = new SceneNode(Guid.NewGuid(), entity.Name, entity);
            (parent ?? _root).AddChild(node);
            if (_isInitialized)
            {
                await node.InitializeAsync();
            }
        }

        public async Task InitializeAsync()
        {
            await _root.InitializeAsync();
            _isInitialized = true;
        }

        public IEnumerable<Entity> GetEntities()
        {
            return GetEntitiesRecursive(_root);
        }

        private IEnumerable<Entity> GetEntitiesRecursive(SceneNode node)
        {
            yield return node.Entity;
            foreach (var child in node.Children)
            {
                foreach (var entity in GetEntitiesRecursive(child))
                {
                    yield return entity;
                }
            }
        }

        internal ValueTask UpdateAsync(double time)
        {
            return _root.UpdateAsync(time);
        }

        public Task RenderAsync(FrameInfo frameInfo)
        {
            return _root.RenderAsync(frameInfo);
        }

        public void Dispose()
        {
            _root.Dispose();
        }
    }
}
