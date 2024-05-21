namespace RockEngine.Vulkan.ECS
{
    public class Scene
    {
        private readonly List<Entity> _entities = new List<Entity>();
        private readonly List<System> _systems = new List<System>();

        public void AddEntity(Entity entity)
        {
            _entities.Add(entity);
        }

        public void AddSystem(System system)
        {
            _systems.Add(system);
        }

        public async Task Update()
        {
            foreach (var system in _systems)
            {
                await system.Update(_entities);
            }
        }
    }
}
