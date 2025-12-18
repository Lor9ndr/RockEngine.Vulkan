using RockEngine.Core.Rendering.ResourceBindings;

using System.Collections;

namespace RockEngine.Core.Internal
{
    public class PerSetBindings : IEnumerable<ResourceBinding>
    {
        private readonly SortedList<UIntRange, ResourceBinding> _bindings = new SortedList<UIntRange, ResourceBinding>();

        public uint Set { get; }
        public int Count => _bindings.Count;
        private bool _needToUpdate;

        public bool NeedToUpdate => _needToUpdate;

        public PerSetBindings(uint set)
        {
            Set = set;
        }

        public void Add(ResourceBinding binding)
        {
            if (binding.SetLocation != Set)
            {
                throw new ArgumentException($"Binding set {binding.SetLocation} doesn't match collection set {Set}");
            }

            _bindings[binding.BindingLocation] = binding;
            CheckForUpdates();
        }

        public bool Remove(ResourceBinding binding)
        {
            return _bindings.Remove(binding.BindingLocation);
        }

        public void CheckForUpdates()
        {
            _needToUpdate = false;
            foreach (var binding in _bindings.Values)
            {
                foreach (var set in binding.DescriptorSets)
                {
                    if (set is null || set.IsDirty)
                    {
                        _needToUpdate = true;
                        return;
                    }
                }
            }

        }

        public void RemoveAll(Func<ResourceBinding, bool> predicate)
        {
            // Use list instead of stackalloc for better memory management
            var keysToRemove = new List<UIntRange>();

            foreach (var item in _bindings)
            {
                if (predicate.Invoke(item.Value))
                {
                    keysToRemove.Add(item.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _bindings.Remove(key);
            }

            CheckForUpdates();
        }

        public unsafe void RemoveAllUnsafe(Func<ResourceBinding, bool> predicate)
        {
            if (Count == 0) return;

            // Limit stack allocation to reasonable size
            const int MAX_STACK_ALLOC = 64;
            var useStackAlloc = Count <= MAX_STACK_ALLOC;

            if (useStackAlloc)
            {
                var locations = stackalloc UIntRange[Count];
                int i = 0;

                foreach (var item in _bindings)
                {
                    if (predicate.Invoke(item.Value))
                    {
                        if (i < Count) // Safety check
                        {
                            locations[i] = item.Key;
                            i++;
                        }
                    }
                }

                while (i > 0)
                {
                    i--;
                    _bindings.Remove(locations[i]);
                }
            }
            else
            {
                // Fallback to heap allocation for large collections
                RemoveAll(predicate);
            }

            CheckForUpdates();
        }

        public IEnumerator<ResourceBinding> GetEnumerator() => _bindings.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Clear()
        {
            _bindings.Clear();  
        }
    }
}
