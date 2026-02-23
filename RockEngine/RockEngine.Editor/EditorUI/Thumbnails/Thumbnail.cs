using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Editor.EditorUI.Thumbnails
{
    public class Thumbnail
    {
        public IAsset Asset { get; }
        public int Width { get; }
        public int Height { get; }
        public Texture? Texture { get; set; }  // GPU texture (for texture assets)

        public Thumbnail(IAsset asset, int width, int height, Texture texture)
        {
            Asset = asset;
            Width = width;
            Height = height;
            Texture = texture;
        }
    }
}
