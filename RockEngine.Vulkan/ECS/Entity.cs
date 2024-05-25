namespace RockEngine.Vulkan.ECS
{
    public class Entity
    {
        public string Name;
        public Transform Transform;
        private readonly Dictionary<Type, Component> _components = new Dictionary<Type, Component>();

        public Entity()
        {
            Name = "Entity";
            Transform = AddComponent(new Transform());
        }

        public T AddComponent<T>(T component) where T : Component
        {
            _components[typeof(T)] = component;
            return component;
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
            foreach (var item in _components)
            {
                await item.Value.Update();
            }
        }
    }
}
