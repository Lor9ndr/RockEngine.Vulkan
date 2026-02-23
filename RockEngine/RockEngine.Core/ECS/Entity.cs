using MessagePack;

using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;

using System.Diagnostics;

using ZLinq;

namespace RockEngine.Core.ECS
{
    [MessagePackObject(AllowPrivate = true)]
    [DebuggerDisplay("Entity - {Name} ({ID})")]
    public partial class Entity
    {
        private static ulong _nextId = 0;
        [IgnoreMember]
        private readonly Lock _componentsLock = new();

        [Key(0)]
        public string Name { get; set; }

        [Key(1)]
        public bool IsActive { get; private set; } = true;

        [Key(2)]
        public ulong ID { get; set; }   // ← now settable, assigned in ctor or deserialization

        [Key(3)]
        private List<IComponent> _components { get; set; } = [];

        [IgnoreMember]
        public IReadOnlyList<IComponent> Components => _components;

        [IgnoreMember]
        public Transform Transform => _components.OfType<Transform>().FirstOrDefault();

        // Parent relationship – serialized ONLY as ParentID
        [Key(4)]
        public ulong? ParentID { get; set; }

        [IgnoreMember]
        public Entity? Parent { get; private set; }

        [IgnoreMember]
        private readonly List<Entity> _children = [];

        [IgnoreMember]
        public IReadOnlyList<Entity> Children => _children.AsReadOnly();

        [Key(5)]
        public RenderLayer Layer { get; set; }

        public event Action OnDestroy;

        public Entity()
        {
            ID = _nextId++;
            AddComponent<Transform>(); // will be overwritten by deserialization – fine
            Name = $"Entity_{ID}";
            Layer = IoC.Container.GetInstance<RenderLayerSystem>().DefaultLayer;
        }

        public T AddComponent<T>() where T : Component
        {
            if (typeof(T) == typeof(Transform) && Transform is not null)
            {
                return (Transform as T)!;
            }

            var component = IoC.Container.GetInstance<T>();
            AddComponent(component);

            return component;
        }
        public IComponent AddComponent(Type componentType) 
        {
            var component = (IComponent)IoC.Container.GetInstance(componentType);
            AddComponent(component);

            return component;
        }

        internal void AddComponent(IComponent component)
        {
            if (component is Transform && Transform is not null)
            {
                return;
            }

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
            return _components.AsValueEnumerable().OfType<T>().FirstOrDefault();
        }

        public void AddChild(Entity child)
        {
            if (child.Parent == this) return;

            child.Parent?.RemoveChild(child);
            child.Parent = this;
            child.ParentID = this.ID;          //  sync ParentID
            _children.Add(child);
            child.Transform.SetParent(this.Transform);
        }

        public bool RemoveChild(Entity child)
        {
            if (!_children.Remove(child)) return false;

            child.Parent = null;
            child.ParentID = null;            // sync ParentID
            child.Transform.SetParent(null);
            return true;
        }

        public async ValueTask Update(WorldRenderer renderer)
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

        public async Task OnStart(WorldRenderer renderer)
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

        public void SetActive(bool isActive = true)
        {
            IsActive = isActive;
            foreach (var item in Components)
            {
                item.SetActive(IsActive);
            }
        }

        public bool HasComponent<T>()
        {
            return _components.OfType<T>().Any();
        }

        public bool TryGetComponent<T>(out T? component)
        {
            component = _components.OfType<T>().FirstOrDefault();
            return component != null;
        }
    }
}
