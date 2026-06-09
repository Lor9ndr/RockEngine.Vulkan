using NUnit.Framework;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Tests.Atlasing
{
    [TestFixture]
    public class AtlasTests : TestBase
    {
        private Atlas _atlas;
        private GridAllocator _gridAllocator;

        [SetUp]
        public void SetUp()
        {
            // 512x512 atlas with 64x64 cells
            _gridAllocator = new GridAllocator();
            _gridAllocator.Configure(64, 64);
            _atlas = new Atlas(
                _context,
                512, 512,
                TextureFormat.R8G8B8A8Unorm,
                _gridAllocator,
                tex => (IntPtr)(_context.GetHashCode()) // dummy TextureId; in real engine use your GetTextureID
            );
        }

        [TearDown]
        public void TearDown()
        {
            _atlas.Dispose();
        }

        [Test]
        public void Constructor_CreatesTextureAndAllocator()
        {
            Assert.That(_atlas.Texture, Is.Not.Null);
            Assert.That(_atlas.Texture.Width, Is.EqualTo(512));
            Assert.That(_atlas.Texture.Height, Is.EqualTo(512));
            Assert.That(_atlas.TextureId, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(_atlas.Width, Is.EqualTo(512));
            Assert.That(_atlas.Height, Is.EqualTo(512));
        }

        [Test]
        public void Allocate_ReturnsRegionWithCorrectUVs()
        {
            var region = _atlas.Allocate(64, 64);
            Assert.That(region, Is.Not.Null);
            Assert.That(region.Page, Is.SameAs(_atlas));
            Assert.That(region.TextureId, Is.EqualTo(_atlas.TextureId));
            // First cell should be at (0,0)
            Assert.That(region.UV0.X, Is.EqualTo(0.0f).Within(0.001));
            Assert.That(region.UV0.Y, Is.EqualTo(0.0f).Within(0.001));
            Assert.That(region.UV1.X, Is.EqualTo(64.0f / 512.0f).Within(0.001));
            Assert.That(region.UV1.Y, Is.EqualTo(64.0f / 512.0f).Within(0.001));
        }

        [Test]
        public void Allocate_SecondRegion_HasDifferentUVs()
        {
            var r1 = _atlas.Allocate(64, 64);
            var r2 = _atlas.Allocate(64, 64);
            Assert.That(r2.UV0.X, Is.GreaterThan(r1.UV0.X).Or.GreaterThan(r1.UV0.Y));
            Assert.That(r2.UV0.Y, Is.GreaterThanOrEqualTo(0));
            // They shouldn't overlap in pixel space
            Assert.That(r2.Rect.Right <= r1.Rect.X || r2.Rect.X >= r1.Rect.Right ||
                        r2.Rect.Bottom <= r1.Rect.Y || r2.Rect.Y >= r1.Rect.Bottom);
        }

        [Test]
        public void Allocate_ThrowsWhenFull()
        {
            // Fill all 64 cells (512/64 = 8 -> 64 cells)
            for (int i = 0; i < 64; i++)
            {
                _atlas.Allocate(64, 64);
            }

            Assert.Throws<InvalidOperationException>(() => _atlas.Allocate(64, 64));
        }

        [Test]
        public void TryAllocate_ReturnsFalseWhenFull()
        {
            for (int i = 0; i < 64; i++)
            {
                _atlas.Allocate(64, 64);
            }

            Assert.That(_atlas.TryAllocate(64, 64, out var region), Is.False);
            Assert.That(region, Is.Null);
        }

        [Test]
        public void Free_MakesSpaceAvailable()
        {
            var r1 = _atlas.Allocate(64, 64);
            _atlas.Allocate(64, 64); // another
            _atlas.Free(r1);
            // Now we can allocate 64x64 again
            Assert.That(_atlas.TryAllocate(64, 64, out var r3), Is.True);
            // It may or may not be the same position, but it must be valid
            Assert.That(r3, Is.Not.Null);
        }

        [Test]
        public void Free_OutsideRegion_DoesNotThrow()
        {
            var allocator = new GridAllocator();
            allocator.Configure(128, 128);
            var otherAtlas = new Atlas(_context, 256, 256, TextureFormat.R8G8B8A8Unorm,
                allocator, tex => IntPtr.Zero);
            var region = otherAtlas.Allocate(128, 128);
            // Freeing on wrong atlas should be a no-op or throw; the Atlas's internal Free validates ownership
            // Currently Atlas.Free is internal and called via region.Free(), which checks page ownership.
            // region.Free() will try to free on _atlas, which is not the page, so it should throw.
            Assert.Throws<ArgumentException>(() => _atlas.Free(region));
            otherAtlas.Dispose();
        }

        [Test]
        public void Region_Free_FreesSpace()
        {
            var region = _atlas.Allocate(64, 64);
            long areaBefore = _gridAllocator.AllocatedArea;
            region.Free();
            Assert.That(_gridAllocator.AllocatedArea, Is.LessThan(areaBefore));
            Assert.That(_atlas.TryAllocate(64, 64, out var newRegion), Is.True);
        }
    }
}