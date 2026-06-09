using System.Numerics;
using RockEngine.Assets;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Editor.EditorUI.Thumbnails
{
    public class Thumbnail
    {
        public IAsset Asset { get; }
        public int Width { get; }
        public int Height { get; }
        public AtlasRegion? AtlasRegion { get; internal set; }   // <-- replaces Texture
        public IntPtr TextureId => AtlasRegion?.TextureId ?? IntPtr.Zero;
        public Vector2 UV0 => AtlasRegion?.UV0 ?? Vector2.Zero;
        public Vector2 UV1 => AtlasRegion?.UV1 ?? Vector2.One;

        public Thumbnail(IAsset asset, int width, int height, AtlasRegion region)
        {
            Asset = asset;
            Width = width;
            Height = height;
            AtlasRegion = region;
        }
    }
}
