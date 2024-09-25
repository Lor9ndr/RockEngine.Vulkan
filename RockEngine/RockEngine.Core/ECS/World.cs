using RockEngine.Core.ECS.Components;

namespace RockEngine.Core.ECS
{
    public class World
    {
        private readonly List<Entity> _entities = new List<Entity>();

        public Entity CreateEntity()
        {
            var entity = new Entity();
            entity.AddComponent(new Transform());
            _entities.Add(entity);
            return entity;
        }
        public IEnumerable<Entity> GetEntities()
        {
            return _entities;
        }
    }
}
