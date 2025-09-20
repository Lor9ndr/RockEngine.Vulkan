using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS
{
    public class Entity
    {
        private static ulong _id = 0;
        private readonly Lock _componentsLock = new Lock();

        public string Name { get;set;}

        public readonly ulong ID;

        private readonly List<IComponent> _components = [];
        public Transform Transform { get; private set; }
        public Entity Parent { get; private set; }
        private readonly List<Entity> _children = new List<Entity>();
        public IReadOnlyList<Entity> Children => _children.AsReadOnly();

        public RenderLayerType Layer { get; set; } = RenderLayerType.Opaque;

        public event Action OnDestroy;

        public Entity()
        {
            ID = _id++;
            Transform = AddComponent<Transform>();
            Name = $"Entity_{ID}";
        }

        public T AddComponent<T>() where T : Component
        {
            if (typeof(T) == typeof(Transform) && Transform is not null)
                return (Transform as T)!;

            var component = IoC.Container.GetInstance<T>();
            AddComponent(component);

            return component;
        }

        internal void AddComponent(IComponent component)
        {
            if (component is Transform && Transform is not null)
                return;

            component.SetEntity(this);
            _components.Add(component);
            World.GetCurrent()?.EnqueueForStart(component);
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
        public IEnumerable<IComponent> Components => _components;

        public void AddChild(Entity child)
        {
            if (child.Parent == this) return;
            child.Parent?.RemoveChild(child);
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
            IComponent[] array;
            lock (_componentsLock)
            {
                array = _components.ToArray();
            }
            for (int i = 0; i < array.Length; i++)
            {
                IComponent? item = array[i];
                if(item is not null)
                {
                    await item.Update(renderer).ConfigureAwait(false);
                }
            }
        }

        public async Task OnStart(Renderer renderer)
        {
            IComponent[] array;
            lock (_componentsLock)
            {
                array = _components.ToArray();
            }
            for (int i = 0; i < array.Length; i++)
            {
                IComponent? item = array[i];
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
