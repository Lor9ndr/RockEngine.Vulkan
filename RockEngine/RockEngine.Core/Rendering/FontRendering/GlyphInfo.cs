using System.Numerics;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Core.Rendering.FontRendering
{
    public struct GlyphInfo
    {
        public AtlasRegion? Region;        // Atlas location (can be null for whitespace/missing)
        public IntPtr TextureId;          // Bindless index
        public Vector2 UV0, UV1;          // Precomputed UVs

        public int Width, Height;
        public int BearingX, BearingY;    // Offset from baseline
        public int Advance;               // Horizontal advance

        public GlyphInfo(AtlasRegion? region, int width, int height,
                         int bearingX, int bearingY, int advance)
        {
            if (region == null)
            {
                Region = null;
                TextureId = IntPtr.Zero;
                UV0 = Vector2.Zero;
                UV1 = Vector2.Zero;
            }
            else
            {
                Region = region;
                TextureId = region.TextureId;
                UV0 = region.UV0;
                UV1 = region.UV1;
            }

            Width = width;
            Height = height;
            BearingX = bearingX;
            BearingY = bearingY;
            Advance = advance;
        }
    }
}