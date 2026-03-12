using RockEngine.Assets;
using RockEngine.Core.Registries;

using System.Collections.Concurrent;

namespace RockEngine.Editor.EditorUI.Thumbnails
{
    public class ThumbnailCache : IRegistry<Thumbnail, IAsset>
    {
        private readonly ConcurrentDictionary<IAsset, Thumbnail> _cache = new();
        public void Dispose()
        {
            _cache.Clear();
        }

        public Thumbnail? Get(IAsset key)
        {
            if(_cache.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        public IEnumerable<Thumbnail> GetAll()
        {
            return _cache.Values;
        }

        public void Register(IAsset key, Thumbnail value)
        {
            _cache[key] = value;
        }

        public void Unregister(IAsset key)
        {
            _cache.Remove(key, out _);
        }
    }
}
