using NUnit.Framework;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Tests.Atlasing
{
    [TestFixture]
    public class GuillotineAllocatorTests
    {
        private GuillotineAllocator _allocator;

        [SetUp]
        public void SetUp()
        {
            _allocator = new GuillotineAllocator();
        }

        [Test]
        public void SingleAllocation_UsesEntirePage()
        {
            _allocator.Reset(100, 100);
            Assert.That(_allocator.Allocate(100, 100, out var rect), Is.True);
            Assert.That(rect.X, Is.EqualTo(0));
            Assert.That(rect.Y, Is.EqualTo(0));
            Assert.That(rect.Width, Is.EqualTo(100));
            Assert.That(rect.Height, Is.EqualTo(100));
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(100 * 100));
        }

        [Test]
        public void TwoAllocations_SplitsCorrectly()
        {
            _allocator.Reset(200, 100);
            // First allocate 100x100
            Assert.That(_allocator.Allocate(100, 100, out var r1), Is.True);
            // Remaining: right 100x100, bottom 100x0? Actually bottom remains full width? Wait: split:
            // rightRemain = 100, bottomRemain = 0
            // Only rightRemain added: 100x100 at (100,0)
            // So second allocation 100x100 should fit in that right region
            Assert.That(_allocator.Allocate(100, 100, out var r2), Is.True);
            Assert.That(r2.X, Is.EqualTo(100));
            Assert.That(r2.Y, Is.EqualTo(0));
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(2 * 100 * 100));
        }

        [Test]
        public void Allocate_ReturnsFalseWhenFull()
        {
            _allocator.Reset(50, 50);
            Assert.That(_allocator.Allocate(50, 50, out _), Is.True);
            Assert.That(_allocator.Allocate(1, 1, out _), Is.False);
        }

        [Test]
        public void Free_MergesAdjacentRects()
        {
            _allocator.Reset(200, 100);
            Assert.That(_allocator.Allocate(100, 100, out var r1), Is.True);
            Assert.That(_allocator.Allocate(100, 100, out var r2), Is.True);

            // Free both rects – they are adjacent and should merge
            _allocator.Free(r1);
            _allocator.Free(r2);

            // Now we should be able to allocate the whole page again
            Assert.That(_allocator.Allocate(200, 100, out var r3), Is.True);
            Assert.That(r3.X, Is.EqualTo(0));
            Assert.That(r3.Y, Is.EqualTo(0));
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(200 * 100)); // only r3 remains
        }

        [Test]
        public void Free_ReducesAllocatedArea()
        {
            _allocator.Reset(128, 128);
            _allocator.Allocate(64, 64, out var r);
            long areaBefore = _allocator.AllocatedArea;
            _allocator.Free(r);
            Assert.That(_allocator.AllocatedArea, Is.LessThan(areaBefore));
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(0));
        }

        [Test]
        public void Reset_ClearsFreeRectsAndArea()
        {
            _allocator.Reset(100, 100);
            _allocator.Allocate(50, 50, out _);
            _allocator.Reset(200, 200);
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(0));
            Assert.That(_allocator.Allocate(200, 200, out _), Is.True);
        }

        [Test]
        public void Allocate_SmallRects_PackEfficiently()
        {
            _allocator.Reset(256, 256);
            // Pack many small rects
            for (int i = 0; i < 64; i++)
            {
                bool ok = _allocator.Allocate(32, 32, out _);
                if (i >= 64)
                    Assert.That(ok, Is.False);
                else
                    Assert.That(ok, Is.True);
            }
            // 256/32 = 8, so 8*8=64 max
            Assert.That(_allocator.Allocate(32, 32, out _), Is.False);
        }
    }
}