using RockEngine.Assets;

namespace RockEngine.Editor.EditorUI.Thumbnails
{
    public interface IThumbnailService
    {
        Task<Thumbnail> GetOrCreateThumbnailAsync(IAsset asset, CancellationToken cancellationToken = default);
        void InvalidateThumbnail(IAsset asset);
    }
}
