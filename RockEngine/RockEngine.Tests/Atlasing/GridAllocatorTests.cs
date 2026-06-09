using NUnit.Framework;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Tests.Atlasing
{
    [TestFixture]
    public class GridAllocatorTests
    {
        private GridAllocator _allocator;

        [SetUp]
        public void SetUp()
        {
            _allocator = new GridAllocator();
            _allocator.Configure(32, 32);
        }

        [Test]
        public void Reset_ClearsState()
        {
            _allocator.Reset(64, 64);
            // Should have 4 cells (64/32 = 2 in each dimension)
            bool ok = _allocator.Allocate(32, 32, out var rect1);
            Assert.That(ok, Is.True);
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(32 * 32));

            _allocator.Reset(64, 64);
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(0));
            // Should be able to allocate first cell again
            ok = _allocator.Allocate(32, 32, out var rect2);
            Assert.That(ok, Is.True);
            Assert.That(rect2, Is.EqualTo(rect1)); // Same position since reset
        }

        [Test]
        public void Allocate_FillsCellsSequentially()
        {
            _allocator.Reset(64, 64); // 2x2 grid
            // First cell
            Assert.That(_allocator.Allocate(32, 32, out var r1), Is.True);
            Assert.That(r1.X, Is.EqualTo(0));
            Assert.That(r1.Y, Is.EqualTo(0));
            // Second
            Assert.That(_allocator.Allocate(32, 32, out var r2), Is.True);
            Assert.That(r2.X, Is.EqualTo(32));
            Assert.That(r2.Y, Is.EqualTo(0));
            // Third
            Assert.That(_allocator.Allocate(32, 32, out var r3), Is.True);
            Assert.That(r3.X, Is.EqualTo(0));
            Assert.That(r3.Y, Is.EqualTo(32));
            // Fourth
            Assert.That(_allocator.Allocate(32, 32, out var r4), Is.True);
            Assert.That(r4.X, Is.EqualTo(32));
            Assert.That(r4.Y, Is.EqualTo(32));
            // No more
            Assert.That(_allocator.Allocate(32, 32, out _), Is.False);
        }

        [Test]
        public void Allocate_RejectsWrongSizes()
        {
            _allocator.Reset(128, 128);
            Assert.That(_allocator.Allocate(64, 64, out _), Is.False);
            Assert.That(_allocator.Allocate(32, 16, out _), Is.False);
            Assert.That(_allocator.Allocate(16, 32, out _), Is.False);
            // Correct size works
            Assert.That(_allocator.Allocate(32, 32, out _), Is.True);
        }

        [Test]
        public void Free_ReusesCell()
        {
            _allocator.Reset(64, 64);
            _allocator.Allocate(32, 32, out var cell1);
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(32 * 32));

            _allocator.Free(cell1);
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(0));
            Assert.That(_allocator.Allocate(32, 32, out var cell2), Is.True);
            Assert.That(cell2, Is.EqualTo(cell1));
        }

        [Test]
        public void Free_InvalidRect_NoError()
        {
            _allocator.Reset(64, 64);
            Assert.DoesNotThrow(() => _allocator.Free(new Rect(100, 100, 32, 32)));
        }

        [Test]
        public void AllocatedArea_AccumulatesAndResets()
        {
            _allocator.Reset(128, 128); // 16 cells
            for (int i = 0; i < 4; i++)
            {
                _allocator.Allocate(32, 32, out _);
            }

            Assert.That(_allocator.AllocatedArea, Is.EqualTo(4 * 32 * 32));
            _allocator.Reset(128, 128);
            Assert.That(_allocator.AllocatedArea, Is.EqualTo(0));
        }
    }
}
