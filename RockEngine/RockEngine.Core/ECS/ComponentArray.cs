using System.Runtime.CompilerServices;


namespace RockEngine.Core.ECS
{
    public class ComponentArray<T> : IComponentArray where T : unmanaged
    {
        private T[] _components;
        private readonly Dictionary<int, int> _entityToIndex = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _indexToEntity = new Dictionary<int, int>();
        private int _size = 0;

        public ComponentArray(int initialCapacity)
        {
            _components = new T[initialCapacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int entityId, in T component)
        {
            if (_size == _components.Length)
            {
                Array.Resize(ref _components, _components.Length * 2);
            }

            _components[_size] = component;
            _entityToIndex[entityId] = _size;
            _indexToEntity[_size] = entityId;
            _size++;
        }

        public ref T this[int entityId]
        {
            get
            {
                return ref _components[_entityToIndex[entityId]];
            }
        }

        public void Remove(int entityId)
        {
            int indexOfRemovedEntity = _entityToIndex[entityId];
            int indexOfLastElement = _size - 1;
            _components[indexOfRemovedEntity] = _components[indexOfLastElement];

            int entityIdOfLastElement = _indexToEntity[indexOfLastElement];
            _entityToIndex[entityIdOfLastElement] = indexOfRemovedEntity;
            _indexToEntity[indexOfRemovedEntity] = entityIdOfLastElement;

            _entityToIndex.Remove(entityId);
            _indexToEntity.Remove(indexOfLastElement);

            _size--;
        }
    }
}
