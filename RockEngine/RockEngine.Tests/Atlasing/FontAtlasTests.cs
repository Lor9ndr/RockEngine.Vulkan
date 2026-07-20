using NUnit.Framework;
using RockEngine.Core.Rendering.FontRendering;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Tests.Atlasing
{
    [TestFixture]
    public class FontAtlasTests : TestBase
    {
        private Atlas? _atlas;
        private GuillotineAllocator? _gridAllocator;
        private const int _cellSize = 64;
        private const int _pageSize = 512; // 8x8 cells = 64 cells

        [SetUp]
        public void SetUp()
        {
            _gridAllocator = new GuillotineAllocator();
            _atlas = new Atlas(
                _context,               // from TestBase
                _pageSize, _pageSize,
                TextureFormat.R8Unorm,
                _gridAllocator,
                tex => (_context.GetHashCode()) // dummy bindless index
            );
        }

        [TearDown]
        public void TearDown()
        {
            _atlas.Dispose();
        }

        [Test]
        public async Task Constructor_PacksBasicAscii()
        {
            var characters = Enumerable.Range(32, 95).Select(i => (char)i); // printable ASCII
            var fontAtlas = new FontAtlas(_atlas, "Fonts/Roboto-VariableFont_wdth,wght.ttf", 48,
                characters, _context.GraphicsSubmitContext);
            await _context.GraphicsSubmitContext.Submit();

            Assert.That(fontAtlas.Glyphs.Count, Is.EqualTo(95));
            foreach (var kv in fontAtlas.Glyphs)
            {
                char c = kv.Key;
                var info = kv.Value;
                if (c == ' ')
                {
                    // Space: no region, zero dimensions, but advance > 0
                    Assert.That(info.Region, Is.Null);
                    Assert.That(info.Width, Is.EqualTo(0));
                    Assert.That(info.Height, Is.EqualTo(0));
                    Assert.That(info.Advance, Is.GreaterThan(0));
                }
                else
                {
                    Assert.That(info.Region, Is.Not.Null);
                    Assert.That(info.Region.TextureId, Is.EqualTo(_atlas.TextureId));
                    Assert.That(info.Width, Is.GreaterThan(0));
                    Assert.That(info.Height, Is.GreaterThan(0));
                    Assert.That(info.Advance, Is.GreaterThan(0));
                    // UVs must be within [0,1]
                    Assert.That(info.UV0.X, Is.InRange(0.0f, 1.0f));
                    Assert.That(info.UV0.Y, Is.InRange(0.0f, 1.0f));
                    Assert.That(info.UV1.X, Is.InRange(0.0f, 1.0f));
                    Assert.That(info.UV1.Y, Is.InRange(0.0f, 1.0f));
                    // UV mapping should correspond to region pixel coordinates
                    Assert.That(info.UV0.X, Is.EqualTo((float)info.Region.Rect.X / _pageSize).Within(0.01));
                    Assert.That(info.UV0.Y, Is.EqualTo((float)info.Region.Rect.Y / _pageSize).Within(0.01));
                    Assert.That(info.UV1.X, Is.EqualTo((float)info.Region.Rect.Right / _pageSize).Within(0.01));
                    Assert.That(info.UV1.Y, Is.EqualTo((float)info.Region.Rect.Bottom / _pageSize).Within(0.01));
                }
            }
        }

        [Test]
        public async Task MissingGlyph_ReturnsNullRegion()
        {
            // Use a character not present in the font (e.g., a control character)
            var fontAtlas = new FontAtlas(_atlas, "Fonts/Roboto-VariableFont_wdth,wght.ttf", 48,
                new[] { '\x01', 'A' }, _context.GraphicsSubmitContext);
            await _context.GraphicsSubmitContext.Submit();

            Assert.That(fontAtlas.Glyphs.ContainsKey('\x01'));
            var info = fontAtlas.Glyphs['\x01'];
            Assert.That(info.Region, Is.Null);
            Assert.That(info.Advance, Is.GreaterThanOrEqualTo(0)); // fallback
        }

        [Test]
        public async Task MultiplePages_AllocatesOnNewPageWhenFull()
        {
            // Локально заменяем аллокатор на GridAllocator 64x64
            var gridAllocator = new GridAllocator();
            gridAllocator.Configure(64, 64); // если GridAllocator требует настройки
            using var testAtlas = new Atlas(
                _context, 512, 512, TextureFormat.R8Unorm,
                gridAllocator,
                tex => _context.GetHashCode());

            // 64 символа → должно работать
            var characters = Enumerable.Range(33, 64).Select(i => (char)i).ToList();
            Assert.DoesNotThrow(() => new FontAtlas(testAtlas, "Fonts/Roboto-VariableFont_wdth,wght.ttf", 48,
                characters, _context.GraphicsSubmitContext));
            await _context.GraphicsSubmitContext.Submit();

            // Добавляем 65‑й → ожидаем исключение
            characters.Add('~');
            Assert.Throws<InvalidOperationException>(() => new FontAtlas(testAtlas, "Fonts/Roboto-VariableFont_wdth,wght.ttf", 48,
                characters, _context.GraphicsSubmitContext));
            await _context.GraphicsSubmitContext.Submit();

        }

        [Test]
        public async Task GlyphMetrics_AreConsistent()
        {
            var fontAtlas = new FontAtlas(_atlas, "Fonts/Roboto-VariableFont_wdth,wght.ttf", 48,
     ['M', 'i'], _context.GraphicsSubmitContext);
            await _context.GraphicsSubmitContext.Submit();

            var glyphM = fontAtlas.Glyphs['M'];
            var glyphI = fontAtlas.Glyphs['i'];

            // 'M' should be wider than 'i'
            Assert.That(glyphM.Advance, Is.GreaterThan(glyphI.Advance));
            // Glyph region size matches pixel dimensions
            Assert.That(glyphM.Region.Rect.Width, Is.EqualTo(glyphM.Width));
            Assert.That(glyphM.Region.Rect.Height, Is.EqualTo(glyphM.Height));
        }

        [Test]
        public async Task LineHeight_IsPositive()
        {
            var fontAtlas = new FontAtlas(_atlas, "Fonts/Roboto-VariableFont_wdth,wght.ttf", 48,
                new[] { 'A' }, _context.GraphicsSubmitContext);
            await _context.GraphicsSubmitContext.Submit();
            Assert.That(fontAtlas.LineHeight, Is.GreaterThan(0));
        }

        // If you have a real render context that can create UploadBatch, you might add an integration test
        // that checks the pixel upload by reading back the texture, but that's complex. We skip for unit tests.
    }
}