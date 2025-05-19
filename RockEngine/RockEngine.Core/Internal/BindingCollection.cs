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
                setBindings = new PerSetBindings(binding.SetLocation);
                _setBindings[binding.SetLocation] = setBindings;
            }

            setBindings.Add(binding);

            if (binding is UniformBufferBinding ubo && ubo.Buffer.IsDynamic)
            {
                _dynamicOffsets.Add((uint)ubo.Offset);
            }
        }

        public bool Remove(ResourceBinding binding)
        {
            if (binding is null)
            {
                return false;
            }
            if (!_setBindings.TryGetValue(binding.SetLocation, out var setBindings))
                return false;

            var removed = setBindings.Remove(binding);

            if (removed && binding is UniformBufferBinding ubo && ubo.Buffer.IsDynamic)
            {
                _dynamicOffsets.Remove((uint)ubo.Offset);
            }

            if (setBindings.Count == 0)
            {
                _setBindings.Remove(binding.SetLocation);
            }

            return removed;
        }

        public IEnumerator<(uint Set, PerSetBindings)> GetEnumerator()
        {
            foreach (var kvp in _setBindings)
            {
                yield return (kvp.Key, kvp.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
    }
}
