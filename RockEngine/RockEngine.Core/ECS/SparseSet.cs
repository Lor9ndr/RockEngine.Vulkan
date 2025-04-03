using System.Collections;

namespace RockEngine.Core.ECS
{
    public sealed class SparseSet<T> : IEnumerable<T>
    {
        private readonly List<T?> _dense = new List<T?>();
        private readonly List<int> _sparse = new List<int>();
        private readonly Stack<int> _freeIndices = new Stack<int>();

        public int Count => _dense.Count - _freeIndices.Count;

        public bool TryGet(int id, out T? entity)
        {
            if (id >= 0 && id < _sparse.Count && _sparse[id] != -1)
            {
                entity = _dense[_sparse[id]];
                return true;
            }
            entity = default;
            return false;
        }

        public void Add(int id, T item)
        {
            while (id >= _sparse.Count) _sparse.Add(-1);
            if (_sparse[id] != -1) return;

            if (_freeIndices.Count > 0)
            {
                int index = _freeIndices.Pop();
                _dense[index] = item;
                _sparse[id] = index;
            }
            else
            {
                _sparse[id] = _dense.Count;
                _dense.Add(item);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in _dense)
            {
                if (item != null) yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}