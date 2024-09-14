using RockEngine.Core.ECS.RockEngine.Core.ECS;
using System.Runtime.CompilerServices;

namespace RockEngine.Core.ECS
{
    public class World : IDisposable
    {
        private const int INITIAL_CAPACITY = 1024;
        private int _nextEntityId = 0;
        private readonly Dictionary<Type, IComponentArray> _componentArrays = new Dictionary<Type, IComponentArray>();
        private readonly Dictionary<int, HashSet<Type>> _entityComponents = new Dictionary<int, HashSet<Type>>();
        private readonly Dictionary<int, int> _entityVersions = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _parentChildRelationships = new Dictionary<int, int>();

        public Entity CreateEntity()
        {
            int id = _nextEntityId++;
            _entityComponents[id] = new HashSet<Type>();
            _entityVersions[id] = 1;
            return new Entity { Id = id, Version = 1 };
        }

        public void DestroyEntity(Entity entity)
        {
            if (!_entityVersions.TryGetValue(entity.Id, out int currentVersion) || currentVersion != entity.Version)
            {
                throw new InvalidOperationException("Attempting to destroy an invalid entity.");
            }

            foreach (var componentType in _entityComponents[entity.Id])
            {
                _componentArrays[componentType].Remove(entity.Id);
            }

            _entityComponents.Remove(entity.Id);
            _entityVersions[entity.Id]++;
            _parentChildRelationships.Remove(entity.Id);
        }

        public void AddComponent<T>(Entity entity, T component) where T : unmanaged
        {
            Type componentType = typeof(T);
            if (!_componentArrays.TryGetValue(componentType, out var array))
            {
                array = new ComponentArray<T>(INITIAL_CAPACITY);
                _componentArrays[componentType] = array;
            }

            ((ComponentArray<T>)array).Add(entity.Id, component);
            _entityComponents[entity.Id].Add(componentType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(Entity entity) where T : unmanaged
        {
            Type componentType = typeof(T);
            if (!_entityComponents[entity.Id].Contains(componentType))
            {
                throw new InvalidOperationException("Entity does not have this component.");
            }

            return ref ((ComponentArray<T>)_componentArrays[componentType])[entity.Id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>(Entity entity) where T : unmanaged
        {
            return _entityComponents[entity.Id].Contains(typeof(T));
        }

        public void SetParent(Entity child, Entity parent)
        {
            _parentChildRelationships[child.Id] = parent.Id;
        }

        public Entity GetParent(Entity child)
        {
            if (_parentChildRelationships.TryGetValue(child.Id, out int parentId))
            {
                return new Entity { Id = parentId, Version = _entityVersions[parentId] };
            }
            return new Entity { Id = -1, Version = 0 }; // No parent
        }

        public void Dispose()
        {
            foreach (var array in _componentArrays.Values)
            {
                if (array is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}

