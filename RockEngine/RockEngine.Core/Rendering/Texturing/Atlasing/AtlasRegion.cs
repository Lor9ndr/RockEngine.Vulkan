using System.Numerics;

namespace RockEngine.Core.Rendering.Texturing.Atlasing
{
    public class AtlasRegion
    {
        public Atlas Page { get; }
        public Rect Rect { get; }                 // Pixel coordinates
        public Vector2 UV0 { get; }
        public Vector2 UV1 { get; }
        public IntPtr TextureId { get; }          // Bindless index / ImGui texture ID

        internal AtlasRegion(Atlas page, Rect rect, IntPtr textureId)
        {
            Page = page;
            Rect = rect;
            TextureId = textureId;
            UV0 = new Vector2(rect.X / (float)page.Width, rect.Y / (float)page.Height);
            UV1 = new Vector2(rect.Right / (float)page.Width, rect.Bottom / (float)page.Height);
        }

        public void Free() => Page.Free(this);
    }
}
