using RockEngine.Core.Rendering.Texturing.Atlasing;
using RockEngine.Vulkan;
using SkiaSharp;

namespace RockEngine.Core.Rendering.FontRendering
{
    public class FontAtlas : IDisposable
    {
        private readonly Atlas _atlas;

        public float LineHeight { get; }
        public IReadOnlyDictionary<char, GlyphInfo> Glyphs { get; }

        /// <summary>
        /// Creates a font atlas by rasterising glyphs with SkiaSharp and packing them into an existing Atlas.
        /// </summary>
        /// <param name="atlas">An atlas page to pack into (should be large enough).</param>
        /// <param name="fontPath">Path to .ttf/.otf file.</param>
        /// <param name="fontSize">Rasterised size in pixels.</param>
        /// <param name="characters">Characters to include.</param>
        /// <param name="submitContext">Used to create an upload batch.</param>
        public FontAtlas(Atlas atlas, string fontPath, float fontSize,
                         IEnumerable<char> characters, SubmitContext submitContext)
        {
            _atlas = atlas;
            var batch = submitContext.CreateBatch();

            using var typeface = SKTypeface.FromFile(fontPath);
            using var font = new SKFont(typeface, fontSize);
            font.Subpixel = false;
            font.Edging = SKFontEdging.Alias; // clean edges for atlas

            var glyphs = new Dictionary<char, GlyphInfo>();
            var paint = new SKPaint { Color = SKColors.White, IsAntialias = false };

            float ascent = -font.Metrics.Ascent; // positive
            float descent = font.Metrics.Descent;
            LineHeight = ascent + descent;

            foreach (char c in characters)
            {
                // Skip already packed (e.g. duplicate)
                if (glyphs.ContainsKey(c))
                {
                    continue;
                }

                ushort glyphId = font.GetGlyph(c);
                if (glyphId == 0)
                {
                    // Отсутствующий глиф: сохраняем с пустым регионом и нулевыми метриками
                    glyphs[c] = new GlyphInfo(null, 0, 0, 0, 0, 0);
                    continue;
                }

                // Measure
                ushort[] singleGlyph = { glyphId };
                float[] widths = font.GetGlyphWidths(singleGlyph);
                int advance = (int)Math.Ceiling(widths[0]);

                font.MeasureText(c.ToString(), out SKRect bounds);
                int width = (int)Math.Ceiling(bounds.Width);
                int height = (int)Math.Ceiling(bounds.Height);

                if (width == 0 || height == 0)
                {
                    // Space or no visual
                    glyphs[c] = new GlyphInfo(null, 0, 0, 0, 0, advance);
                    continue;
                }

                // Rasterise glyph to bitmap
                using var bitmap = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.Black);
                canvas.DrawText(c.ToString(), -bounds.Left, -bounds.Top, SKTextAlign.Left, font, paint);
                canvas.Flush();

                // Allocate region in atlas (throws if full – handle with a new page if needed)
                if (!_atlas.TryAllocate(width, height, out var region))
                {
                    throw new InvalidOperationException($"Atlas full when packing character '{c}'");
                }

                // Upload pixels (format R8)
                var pixelSpan = bitmap.GetPixelSpan();
                _atlas.UploadPixels(batch, region, pixelSpan);

                int bearingX = (int)Math.Ceiling(-bounds.Left);
                int bearingY = (int)Math.Ceiling(-bounds.Top);

                glyphs[c] = new GlyphInfo(region, width, height, bearingX, bearingY, advance);
            }

            // Submit all uploads
            batch.Submit();

            Glyphs = glyphs;
        }

        public void Dispose()
        {
            // The atlas page itself is managed outside; we don't free regions automatically.
            // If you want to reclaim space, iterate Glyphs and call region.Free().
        }
    }
}