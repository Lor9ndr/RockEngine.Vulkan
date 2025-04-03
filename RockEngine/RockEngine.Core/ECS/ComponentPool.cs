using RockEngine.Core.ECS.Components;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.ECS
{
    public interface IComponentPool : IDisposable
    {
        Type ComponentType { get; }
        void Remove(int entityId);
        bool HasComponent(int entityId);
    }

    public sealed class ComponentPool<T> : IComponentPool where T : struct, IComponent
    {
        private readonly Dictionary<int, T> _components = new Dictionary<int, T>();

        public Type ComponentType => typeof(T);
        public IEnumerable<int> GetEntityIds() => _components.Keys;
        public IEnumerable<T> GetAllComponents() => _components.Values;

        public void Add(int entityId, in T component)
        {
            _components[entityId] = component;
        }

        public ref T Get(int entityId)
        {
            ref T value = ref CollectionsMarshal.GetValueRefOrNullRef(_components, entityId);
            if (Unsafe.IsNullRef(ref value))
            {
                throw new KeyNotFoundException($"Entity {entityId} has no {typeof(T).Name} component");
            }
            return ref value;
        }

        public bool TryGetComponent(int id, out T component)
        {
            ref T valueRef = ref CollectionsMarshal.GetValueRefOrNullRef(_components, id);
            if (!Unsafe.IsNullRef(ref valueRef))
            {
                component = valueRef;
                return true;
            }
            component = default;
            return false;
        }

        public bool HasComponent(int entityId) => _components.ContainsKey(entityId);
        public void Remove(int entityId) => _components.Remove(entityId);
        public void Dispose() => _components.Clear();
    }
}