using System.Numerics;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Core.ECS.Components.UI
{
    public class UIImage : UIRenderable
    {
        public AtlasRegion? Sprite { get; set; }
        public override AtlasRegion? TextureRegion => Sprite;

        public override void WriteVertexData(Span<UIVertex> vertices, Vector2 canvasSize, RectTransform rt)
        {
            var rect = rt.GetRect(canvasSize);
            Vector2 pos0 = new(rect.Left, rect.Top);
            Vector2 pos1 = new(rect.Right, rect.Top);
            Vector2 pos2 = new(rect.Right, rect.Bottom);
            Vector2 pos3 = new(rect.Left, rect.Bottom);

            Vector2 uv0 = Sprite?.UV0 ?? Vector2.Zero;
            Vector2 uv1 = Sprite?.UV1 ?? Vector2.One;

            uint texId = Sprite?.TextureId != IntPtr.Zero ? (uint)Sprite.TextureId.ToInt64() : 0;
            Vector4 col = Color;

            vertices[0] = new UIVertex { Position = pos0, TexCoord = uv0, Color = col, TextureId = texId };
            vertices[1] = new UIVertex { Position = pos1, TexCoord = new Vector2(uv1.X, uv0.Y), Color = col, TextureId = texId };
            vertices[2] = new UIVertex { Position = pos2, TexCoord = uv1, Color = col, TextureId = texId };
            vertices[3] = new UIVertex { Position = pos3, TexCoord = new Vector2(uv0.X, uv1.Y), Color = col, TextureId = texId };
        }
    }
}