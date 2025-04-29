using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS
{
    public class Entity
    {
        private static ulong _id = 0;

        public readonly ulong ID;

        private readonly List<IComponent> _components = [];
        public Transform Transform { get; private set; }
        public Entity Parent { get; private set; }
        private readonly List<Entity> _children = new List<Entity>();
        public IReadOnlyList<Entity> Children => _children.AsReadOnly();

        public RenderLayerType Layer { get; private set; } = RenderLayerType.Opaque;

        public event Action OnDestroy;

        public Entity()
        {
            ID = _id++;
            Transform = AddComponent<Transform>();
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T();
            component.SetEntity(this);
            _components.Add(component);
            return component;
        }

        public bool RemoveComponent<T>(T component) where T : IComponent
        {
            if (component is Transform)
            {
                throw new Exception("Can not remove Transform from entity");
            }
            return _components.Remove(component);
        }

        public T? GetComponent<T>() where T : IComponent
        {
            return _components.OfType<T>().FirstOrDefault();
        }

        public void AddChild(Entity child)
        {
            if (child.Parent == this) return;
            if (child.Parent != null)
            {
                child.Parent.RemoveChild(child);
            }
            child.Parent = this;
            _children.Add(child);
            child.Transform.SetParent(this.Transform);
        }

        public bool RemoveChild(Entity child)
        {
            if (!_children.Remove(child)) return false;
            child.Parent = null;
            child.Transform.SetParent(null);
            return true;
        }

        public async ValueTask Update(Renderer renderer)
        {
            foreach (var item in _components)
            {
                await item.Update(renderer).ConfigureAwait(false);
            }
        }

        public async ValueTask OnStart(Renderer renderer)
        {
            foreach (var item in _components)
            {
                await item.OnStart(renderer).ConfigureAwait(false);
            }
        }

        public void Destroy()
        {
            foreach (var item in _components)
            {
                item.Destroy();
            }
        }
    }
}
