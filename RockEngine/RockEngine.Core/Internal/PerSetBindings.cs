using RockEngine.Core.Rendering.ResourceBindings;

using System.Collections;

namespace RockEngine.Core.Internal
{
    public class PerSetBindings : IEnumerable<ResourceBinding>
    {
        private readonly SortedList<uint, ResourceBinding> _bindings = new SortedList<uint, ResourceBinding>();

        public uint Set { get; }
        public int Count => _bindings.Count;
        public bool NeedToUpdate => _bindings.Values.Any(b => b.DescriptorSets.Any(s=>s is null || s.IsDirty));

        public PerSetBindings(uint set)
        {
            Set = set;
        }

        public void Add(ResourceBinding binding)
        {
            if (binding.SetLocation != Set)
                throw new ArgumentException($"Binding set {binding.SetLocation} doesn't match collection set {Set}");

            _bindings[binding.BindingLocation] = binding;
        }

        public bool Remove(ResourceBinding binding)
        {
            return _bindings.Remove(binding.BindingLocation);
        }
        public unsafe void RemoveAll(Func<ResourceBinding, bool> value)
        {
            var locations = stackalloc uint[Count];
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
    }
}
