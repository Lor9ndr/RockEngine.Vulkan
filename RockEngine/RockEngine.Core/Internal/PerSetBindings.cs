using RockEngine.Core.Rendering.ResourceBindings;

using System.Collections;

using ZLinq;

namespace RockEngine.Core.Internal
{
    public class PerSetBindings : IEnumerable<ResourceBinding>
    {
        private readonly SortedList<UIntRange, ResourceBinding> _bindings = new SortedList<UIntRange, ResourceBinding>();
        private bool _needToUpdate;

        public uint Set { get; }
        public int Count => _bindings.Count;

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
            bool remove = _bindings.Remove(binding.BindingLocation);
            CheckForUpdates();
            return remove;
        }

        public void CheckForUpdates()
        {
            _needToUpdate = _bindings.AsValueEnumerable().Any(s => s.Value.DescriptorSets.AsValueEnumerable().Any(s => s.Value.AsValueEnumerable().Any(s => s is null || s.IsDirty)));
        }

        public void RemoveAll(Func<ResourceBinding, bool> predicate)
        {
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

        public IEnumerator<ResourceBinding> GetEnumerator() => _bindings.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Clear()
        {
            _bindings.Clear();
        }
    }
}