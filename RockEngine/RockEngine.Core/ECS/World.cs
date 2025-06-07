using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;

using System.Collections.Concurrent;

using ZLinq;

namespace RockEngine.Core.ECS
{
    public class World : IDisposable
    {
        private readonly List<Entity> _entities = new List<Entity>();
        private static World _singleton;
        private enum WorldState { NotStarted, Starting, Started }
        private WorldState _state = WorldState.NotStarted;
        private readonly ConcurrentQueue<IComponent> _pendingStartComponents = new();
        private readonly object _stateLock = new();

        internal static World GetCurrent()
        {
            return _singleton;
        }

        public World()
        {
            if (_singleton == null)
            {
                _singleton = this;
            }
            else
            {
                throw new InvalidOperationException("Only one world can exists");
            }
        }

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
        public List<Entity> GetEntitiesWithComponent<T>() where T : IComponent
        {
            return _entities.AsValueEnumerable().Where(s=>s.GetComponent<T>() is not null).ToList();
        }

        public async Task Start(Renderer renderer)
        {
            lock (_stateLock)
            {
                if (_state != WorldState.NotStarted) return;
                _state = WorldState.Starting;
            }

            // Process existing entities
            foreach (var entity in _entities)
            {
                await ProcessEntityComponents(entity, renderer);
            }

            lock (_stateLock) _state = WorldState.Started;
            await ProcessPendingStarts(renderer);
        }

        private async Task ProcessEntityComponents(Entity entity, Renderer renderer)
        {
            foreach (var component in entity.Components)
            {
                await component.OnStart(renderer);
            }
        }

        private async Task ProcessPendingStarts(Renderer renderer)
        {
            while (_pendingStartComponents.TryDequeue(out var component))
            {
                await component.OnStart(renderer);
            }
        }

        public void EnqueueForStart(IComponent component)
        {
            lock (_stateLock)
            {
                if (_state == WorldState.Started)
                {
                    _pendingStartComponents.Enqueue(component);
                }
            }
        }

        public async ValueTask Update(Renderer renderer)
        {
            await ProcessPendingStarts(renderer);
            foreach (var entity in _entities.ToArray())
            {
                await entity.Update(renderer);
            }
        }

        internal Entity AddEntity(EntityData entityData)
        {

            var entity = new Entity();
            _entities.Add(entity);

            // instatiate components by their data and so on

            return entity;
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
