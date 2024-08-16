using System.Collections;
using System.Collections.Generic;

namespace RockEngine.Vulkan.ECS
{
    internal class ComponentCollection : IEnumerable<Component>
    {
        private readonly Dictionary<Type, List<Component>> _components;
        private readonly List<IRenderable> _renderables;


        public ComponentCollection()
        {
            _components = new Dictionary<Type, List<Component>>();
            _renderables = new List<IRenderable>();
        }

        public void Add<T>(T component) where T : Component
        {
            var type = typeof(T);
            if (!_components.TryGetValue(type, out var componentList))
            {
                componentList = new List<Component>();
                _components[type] = componentList;
            }
            // Add the component to the list
            componentList.Add(component);

            // Insert the component into the list in sorted order
            if (component is IRenderable renderable)
            {
                InsertSorted(_renderables, renderable);
            }
        }

        public void Remove<T>(T component) where T : Component
        {
            if (_components.TryGetValue(typeof(T), out var components))
            {
                components.Remove(component);
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
            foreach (var componentList in _components.Values)
            {
                foreach (var component in componentList)
                {
                    yield return component;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void InsertSorted(List<IRenderable> componentList, IRenderable component)
        {
            int index = componentList.BinarySearch(component, new RenderableComparer());
            if (index < 0)
            {
                index = ~index; // Get the index where the component should be inserted
            }
            componentList.Insert(index, component);
        }
        // Get the first component of type T
        public T? GetFirst<T>() where T : Component
        {
            if (_components.TryGetValue(typeof(T), out var componentList) && componentList.Count > 0)
            {
                return (T)componentList[0]; // Return the first component
            }
            return null; // Return null if no component of type T exists
        }

        // Get a list of all components of type T
        public List<T> GetList<T>() where T : Component
        {
            if (_components.TryGetValue(typeof(T), out var componentList))
            {
                return componentList.ConvertAll(component => (T)component); // Convert to List<T>
            }
            return new List<T>(); // Return an empty list if no components of type T exist
        }

        public IEnumerable<IRenderable> GetRenderables()
        {
            return _renderables;
        }

        // Comparer for sorting components by Order
        private class RenderableComparer : IComparer<IRenderable>
        {
            public int Compare(IRenderable x, IRenderable y)
            {
                return x.Order.CompareTo(y.Order);
            }
        }

       
    }
}
