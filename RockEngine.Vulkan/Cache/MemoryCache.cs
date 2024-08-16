using System.Collections.Concurrent;

namespace RockEngine.Vulkan.Cache
{

    /// <summary>
    /// Cache data in memory
    /// </summary>
    /// <typeparam name="TKey">key type</typeparam>
    /// <typeparam name="TValue">value type</typeparam>
    public class MemoryCache<TKey, TValue> : ICache<TKey, TValue> where TKey :notnull
    {
        protected readonly ConcurrentDictionary<TKey, TValue> _cache = new ConcurrentDictionary<TKey, TValue>();

        /// <inheritdoc/>
        public bool TryGet(TKey key, out TValue value) => _cache.TryGetValue(key, out value);

        /// <inheritdoc/>
        public void Set(TKey key, TValue value) => _cache[key] = value;
    }
}
