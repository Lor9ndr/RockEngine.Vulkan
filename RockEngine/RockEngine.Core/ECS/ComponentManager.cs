using RockEngine.Core.ECS.Components;

namespace RockEngine.Core.ECS
{
    public sealed class ComponentManager : IDisposable
    {
        private readonly Dictionary<Type, IComponentPool> _pools = new Dictionary<Type, IComponentPool>();

        public ComponentPool<T> GetPool<T>() where T : struct, IComponent
        {
            var type = typeof(T);
            if (!_pools.TryGetValue(type, out var pool))
            {
                pool = new ComponentPool<T>();
                _pools[type] = pool;
            }
            return (ComponentPool<T>)pool;
        }

        public void Dispose()
        {
            foreach (var pool in _pools.Values)
            {
                pool.Dispose();
            }
        }
    }
}