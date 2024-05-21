

namespace RockEngine.Vulkan.ECS
{
    public class Entity
    {
        private readonly Dictionary<Type, Component> _components = new Dictionary<Type, Component>();

        public void AddComponent<T>(T component) where T : Component
        {
            _components[typeof(T)] = component;
        }

        public T GetComponent<T>() where T : Component
        {
            if(!_components.TryGetValue(typeof(T), out var component))
            {
                throw new Exception($"Failed to find a component of type {typeof(T)}");
            }
            return (T)component;
        }

        public async Task Update()
        {
        }
    }
}
