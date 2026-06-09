using System.Collections;
using RockEngine.Core.Rendering.ResourceBindings;
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
            lock (_bindings)
            {
                foreach (var binding in _bindings.Values)
                {
                    foreach (var descriptors in binding.DescriptorSets.Values)
                    {
                        foreach (var descriptor in descriptors)
                        {
                            if (descriptor is null || descriptor.IsDirty)
                            {
                                _needToUpdate = true;
                                return;
                            }
                        }
                    }
                }
            }
           
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

        public ResourceBinding? GetBinding(uint bindingNumber)
        {
            foreach (var kv in _bindings)
            {
                if (kv.Key.Contains(bindingNumber))
                {
                    return kv.Value;
                }
            }
            return null;
        }

        public IEnumerator<ResourceBinding> GetEnumerator() => _bindings.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Clear()
        {
            _bindings.Clear();
        }


    }
}