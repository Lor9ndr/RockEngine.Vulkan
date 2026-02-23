
using NLog;

using RockEngine.Assets;
using RockEngine.Core.Registries;

namespace RockEngine.Editor.EditorUI.Thumbnails
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly IThumbnailRenderer _renderer;
        private readonly IRegistry<Thumbnail, IAsset> _cache;
        private static readonly Logger  _logger = LogManager.GetCurrentClassLogger();

        public ThumbnailService(IThumbnailRenderer renderer, IRegistry<Thumbnail, IAsset> cache)
        {
            _renderer = renderer;
            _cache = cache;
        }

        public async Task<Thumbnail> GetOrCreateThumbnailAsync(IAsset asset, CancellationToken cancellationToken = default)
        {
            var cached = _cache.Get(asset);
            if (cached is not null)
            {
                return cached;
            }

            try
            {
                var thumbnail = await _renderer.RenderThumbnailAsync(asset, cancellationToken: cancellationToken);
                _cache.Register(asset, thumbnail);
                return thumbnail;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to render thumbnail for {Asset}", asset.Name);
                // Return a placeholder thumbnail? or rethrow
                throw;
            }
        }

        public void InvalidateThumbnail(IAsset asset)
        {
            _cache.Unregister(asset);
        }
    }
}
