using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS
{
    public class World : IDisposable
    {
        private readonly List<Entity> _entities = new List<Entity>();

        public Entity CreateEntity()
        {
            var entity = new Entity();
            _entities.Add(entity);
            return entity;
        }

        public void RemoveEntity(Entity entity)
        {
            _entities.Remove(entity);
        }

        public IEnumerable<Entity> GetEntities()
        {
            return _entities;
        }

        public async ValueTask Start(Renderer renderer)
        {
            foreach (var item in _entities)
            {
                await item.OnStart(renderer).ConfigureAwait(false);
            }
        }

        public async ValueTask Update(Renderer renderer)
        {
            foreach (var entity in _entities)
            {
                await entity.Update(renderer).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            foreach (var item in _entities)
            {
                item.Destroy();
            }
        }
    }
}
