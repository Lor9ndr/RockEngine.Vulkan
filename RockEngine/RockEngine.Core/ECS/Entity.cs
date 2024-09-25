using RockEngine.Core.ECS.Components;

namespace RockEngine.Core.ECS
{
    public class Entity
    {
        public List<IComponent> components = new List<IComponent>();

        public void AddComponent<T>(T component) where T : IComponent
        {
            components.Add(component);
        }
        public void RemoveComponent<T>(T component) where T : IComponent
        {
            components.Remove(component);
        }
        public T GetComponent<T>() where T : IComponent
        {
           return components.OfType<T>().FirstOrDefault() ?? throw new InvalidOperationException($"Failed to find component of type {typeof(T)}");
        }
    }
}
