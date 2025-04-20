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
        public RenderLayerType Layer { get; private set; } = RenderLayerType.Opaque;

        public event Action OnDestroy;

        public Entity()
        {
            ID = _id++;
            Transform = AddComponent<Transform>();
        }

        public T AddComponent<T>() where T : IComponent, new()
        {
            var component = new T();
            _components.Add(component);
            component.SetEntity(this);
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
