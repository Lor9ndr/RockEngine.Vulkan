using System.Collections;
using RockEngine.Core.Rendering.ResourceBindings;

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

            lock (_bindings)
            {
                _bindings[binding.BindingLocation] = binding;
                CheckForUpdates(); // вызов внутри блокировки (реентерабельно)
            }
        }

        public bool Remove(ResourceBinding binding)
        {
            lock (_bindings)
            {
                bool remove = _bindings.Remove(binding.BindingLocation);
                CheckForUpdates();
                return remove;
            }
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
            lock (_bindings)
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
        }

        public ResourceBinding? GetBinding(uint bindingNumber)
        {
            lock (_bindings) // защита от изменений во время перебора
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
        }

        public IEnumerator<ResourceBinding> GetEnumerator()
        {
            // Возвращает снепшот или обёртку с блокировкой – на усмотрение.
            // Здесь простой вариант: получить перечислитель под блокировкой,
            // но он остаётся привязанным к живой коллекции, что небезопасно.
            // Лучше материализовать список.
            lock (_bindings)
            {
                return _bindings.Values.GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Clear()
        {
            lock (_bindings)
            {
                _bindings.Clear();
                CheckForUpdates(); // если нужно сбросить флаг
            }
        }
    }
}