using RockEngine.Core.Rendering.ResourceBindings;

using System.Collections;

namespace RockEngine.Core.Internal
{
    public class BindingCollection : IEnumerable<(uint Set, PerSetBindings)>
    {
        private readonly SortedDictionary<uint, PerSetBindings> _setBindings
            = new SortedDictionary<uint, PerSetBindings>();
        private readonly List<uint> _dynamicOffsets = new List<uint>();

        public uint MinSetLocation => _setBindings.Keys.FirstOrDefault();
        public uint MaxSetLocation => _setBindings.Keys.LastOrDefault();
        public int CountAllBindings => _setBindings.Sum(kvp => kvp.Value.Count);
        public int Count => _setBindings.Count;
        internal List<uint> DynamicOffsets => _dynamicOffsets;

        public void Add(ResourceBinding binding)
        {
            if (!_setBindings.TryGetValue(binding.SetLocation, out var setBindings))
            {
                // Only create PerSetBindings when we actually have a binding to add
                setBindings = new PerSetBindings(binding.SetLocation);
                _setBindings[binding.SetLocation] = setBindings;
            }
            else
            {

            }

            setBindings.Add(binding);

            if (binding is UniformBufferBinding ubo && ubo.Buffer.IsDynamic)
            {
                _dynamicOffsets.Add((uint)ubo.Offset);
            }
        }


        public bool Remove(ResourceBinding binding)
        {
            if (binding is null) return false;

            if (!_setBindings.TryGetValue(binding.SetLocation, out var setBindings))
                return false;

            var removed = setBindings.Remove(binding);

            if (removed)
            {
                if (binding is UniformBufferBinding ubo && ubo.Buffer.IsDynamic)
                {
                    _dynamicOffsets.Remove((uint)ubo.Offset);
                }

                // Remove the entire PerSetBindings if it's now empty
                if (setBindings.Count == 0)
                {
                    _setBindings.Remove(binding.SetLocation);
                }
            }

            return removed;
        }

        public Enumerator GetEnumerator() => new Enumerator(_setBindings);

        IEnumerator<(uint Set, PerSetBindings)> IEnumerable<(uint Set, PerSetBindings)>.GetEnumerator()
             => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public bool TryGetBindings(uint set, out PerSetBindings bindings)
        {
            return _setBindings.TryGetValue(set, out bindings);
        }

        internal void RemoveAll(Func<ResourceBinding, bool> value)
        {
            foreach (var set in _setBindings)
            {
                set.Value.RemoveAll(value);
            }
        }

        internal void Clear()
        {
            foreach (var item in _setBindings)
            {
                item.Value.Clear();
            }
        }
        public struct Enumerator : IEnumerator<(uint Set, PerSetBindings)>
        {
            private SortedDictionary<uint, PerSetBindings>.Enumerator _inner;

            internal Enumerator(SortedDictionary<uint, PerSetBindings> bindings)
            {
                _inner = bindings.GetEnumerator();
            }

            public (uint Set, PerSetBindings) Current
                => (_inner.Current.Key, _inner.Current.Value);

            object IEnumerator.Current => Current;

            public bool MoveNext() => _inner.MoveNext();

            public void Reset() => throw new NotSupportedException();

            public void Dispose() => _inner.Dispose();
        }
    }
}
