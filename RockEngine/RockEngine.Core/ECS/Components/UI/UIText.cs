using System.Numerics;
using MemoryPack;
using RockEngine.Core.Rendering.FontRendering;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Core.ECS.Components.UI
{
    [MemoryPackable]
    public partial class UIText : UIRenderable
    {
        public string Text { get; set; } = "Hello world";
        [MemoryPackIgnore]
        public FontAtlas? Font { get; set; }
        public override AtlasRegion? TextureRegion => null; // Font atlas accessed directly

        public override void WriteVertexData(Span<UIVertex> vertices, Vector2 canvasSize, RectTransform rt)
        {
            if (Font == null || string.IsNullOrEmpty(Text))
            {
                // Fill with zero-sized quad or skip; here we write zeroed vertices
                for (int i = 0; i < 4; i++)
                {
                    vertices[i] = default;
                }

                return;
            }

            var rect = rt.GetRect(canvasSize);
            Vector2 pen = new Vector2(rect.Left, rect.Top);
            float scale = rt.SizeDelta.Y / Font.LineHeight; // approximate scaling
            int iChar = 0;

            foreach (char c in Text)
            {
                if (!Font.Glyphs.TryGetValue(c, out GlyphInfo glyph))
                {
                    continue;
                }

                if (glyph.Region == null)
                {
                    pen.X += glyph.Advance * scale;
                    continue;
                }

                float x0 = pen.X + glyph.BearingX * scale;
                float y0 = pen.Y + (glyph.BearingY - glyph.Height) * scale; // flip Y?
                float x1 = x0 + glyph.Width * scale;
                float y1 = y0 + glyph.Height * scale;

                // Write quad for this glyph
                var verts = vertices.Slice(iChar * 4, 4);
                uint texId = glyph.TextureId != IntPtr.Zero ? (uint)glyph.TextureId.ToInt64() : 0;
                Vector4 col = Color;

                verts[0] = new UIVertex { Position = new Vector2(x0, y0), TexCoord = glyph.UV0, Color = col, TextureId = texId };
                verts[1] = new UIVertex { Position = new Vector2(x1, y0), TexCoord = new Vector2(glyph.UV1.X, glyph.UV0.Y), Color = col, TextureId = texId };
                verts[2] = new UIVertex { Position = new Vector2(x1, y1), TexCoord = glyph.UV1, Color = col, TextureId = texId };
                verts[3] = new UIVertex { Position = new Vector2(x0, y1), TexCoord = new Vector2(glyph.UV0.X, glyph.UV1.Y), Color = col, TextureId = texId };

                pen.X += glyph.Advance * scale;
                iChar++;
            }
        }
    }
}