using System.Collections;

namespace RockEngine.Vulkan.ECS
{
    internal class ComponentCollection : IEnumerable<Component>
    {
        private readonly Dictionary<Type, object> _components;
        private readonly List<IRenderable> _renderables;

        public ComponentCollection()
        {
            _components = new Dictionary<Type, object>();
            _renderables = new List<IRenderable>();
        }

        public void Add<T>(T component) where T : Component
        {
            var type = typeof(T);
            if (!_components.TryGetValue(type, out var existing))
            {
                _components[type] = component;
            }
            else if (existing is List<Component> list)
            {
                list.Add(component);
            }
            else
            {
                var newList = new List<Component>(2) { (Component)existing, component };
                _components[type] = newList;
            }

            if (component is IRenderable renderable)
            {
                InsertSortedRenderable(renderable);
            }
        }

        public void Remove<T>(T component) where T : Component
        {
            var type = typeof(T);
            if (_components.TryGetValue(type, out var existing))
            {
                if (existing is List<Component> list)
                {
                    list.Remove(component);
                    if (list.Count == 1)
                    {
                        _components[type] = list[0];
                    }
                    else if (list.Count == 0)
                    {
                        _components.Remove(type);
                    }
                }
                else if (existing.Equals(component))
                {
                    _components.Remove(type);
                }
            }

            if (component is IRenderable renderable)
            {
                _renderables.Remove(renderable);
            }
        }

        public void Clear()
        {
            _components.Clear();
            _renderables.Clear();
        }

        public IEnumerator<Component> GetEnumerator()
        {
            foreach (var component in _components.Values)
            {
                if (component is Component singleComponent)
                {
                    yield return singleComponent;
                }
                else if (component is List<Component> componentList)
                {
                    foreach (var c in componentList)
                    {
                        yield return c;
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public T? GetFirst<T>() where T : Component
        {
            if (_components.TryGetValue(typeof(T), out var component))
            {
                return component as T ?? (component as List<Component>)?[0] as T;
            }
            return null;
        }

        public IEnumerable<T> GetList<T>() where T : Component
        {
            if (_components.TryGetValue(typeof(T), out var component))
            {
                if (component is T singleComponent)
                {
                    yield return singleComponent;
                }
                else if (component is List<Component> componentList)
                {
                    for (int i = 0; i < componentList.Count; i++)
                    {
                        if (componentList[i] is T typedComponent)
                        {
                            yield return typedComponent;
                        }
                    }
                }
            }
        }

        public IReadOnlyList<IRenderable> GetRenderables() => _renderables;

        private void InsertSortedRenderable(IRenderable renderable)
        {
            int left = 0;
            int right = _renderables.Count - 1;

            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                int comparison = renderable.Order.CompareTo(_renderables[middle].Order);

                if (comparison == 0)
                {
                    _renderables.Insert(middle, renderable);
                    return;
                }
                else if (comparison < 0)
                {
                    right = middle - 1;
                }
                else
                {
                    left = middle + 1;
                }
            }

            _renderables.Insert(left, renderable);
        }
    }
}
