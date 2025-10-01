using RockEngine.Core.Rendering.ResourceBindings;

using System.Collections;

using ZLinq;

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
                throw new ArgumentException($"Binding set {binding.SetLocation} doesn't match collection set {Set}");

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
        public unsafe void RemoveAll(Func<ResourceBinding, bool> value)
        {
            var locations = stackalloc UIntRange[Count];
            int i = 0;
            foreach (var item in _bindings)
            {
                if (value.Invoke(item.Value))
                {
                    locations[i] = item.Key;
                    i++;
                }
            }
            while(i > 0)
            {
                _bindings.Remove(locations[i]);
                i--;
            }
        }

        public IEnumerator<ResourceBinding> GetEnumerator() => _bindings.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Clear()
        {
            _bindings.Clear();  
        }
    }
}
