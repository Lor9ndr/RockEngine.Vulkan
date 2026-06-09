using NUnit.Framework;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Tests.Atlasing
{
    [TestFixture]
    public class MaxRectsAllocatorTests
    {
        [Test]
        public void SingleAlloc_FillsEntirePage()
        {
            var alloc = new MaxRectsAllocator(MaxRectsAllocator.FitHeuristic.BestShortSideFit);
            alloc.Reset(128, 256);
            Assert.That(alloc.Allocate(128, 256, out var rect), Is.True);
            Assert.That(rect.X, Is.EqualTo(0));
            Assert.That(rect.Y, Is.EqualTo(0));
            Assert.That(alloc.AllocatedArea, Is.EqualTo(128 * 256));
        }

        [Test]
        public void MultipleAllocs_PackWithoutOverlap()
        {
            var alloc = new MaxRectsAllocator();
            alloc.Reset(1024, 1024);
            var rects = new List<Rect>();
            for (int i = 0; i < 50; i++)
            {
                int w = 64 + i % 32;
                int h = 64 + (i / 2) % 32;
                if (alloc.Allocate(w, h, out var rect))
                    rects.Add(rect);
            }
            // No overlaps
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i];
                    var b = rects[j];
                    Assert.That(a.Right <= b.X || a.X >= b.Right || a.Bottom <= b.Y || a.Y >= b.Bottom,
                        $"Rects {i} and {j} overlap");
                }
        }

        [Test]
        public void Free_AllowsReallocationOfSpace()
        {
            var alloc = new MaxRectsAllocator(MaxRectsAllocator.FitHeuristic.BestAreaFit);
            alloc.Reset(200, 200);
            alloc.Allocate(100, 200, out var r1);
            alloc.Allocate(100, 100, out var r2);
            alloc.Free(r1);
            // Should be able to allocate 100x200 again
            Assert.That(alloc.Allocate(100, 200, out var r3), Is.True);
            // r3 may be in the same position as r1 or after merging
            Assert.That(r3.X, Is.EqualTo(0).Or.EqualTo(100));
            Assert.That(r3.Y, Is.EqualTo(0));
            Assert.That(r3.Width, Is.EqualTo(100));
            Assert.That(r3.Height, Is.EqualTo(200));
        }

        [Test]
        public void DifferentHeuristics_ProduceDifferentPlacements()
        {
            var rectsBSF = new List<Rect>();
            var rectsBL = new List<Rect>();

            int[] sizes = { 100, 50, 75, 120, 30 };
            // Run BestShortSideFit
            var alloc1 = new MaxRectsAllocator(MaxRectsAllocator.FitHeuristic.BestShortSideFit);
            alloc1.Reset(512, 512);
            foreach (int s in sizes)
                if (alloc1.Allocate(s, s, out var r))
                    rectsBSF.Add(r);

            // Run BottomLeft
            var alloc2 = new MaxRectsAllocator(MaxRectsAllocator.FitHeuristic.BottomLeft);
            alloc2.Reset(512, 512);
            foreach (int s in sizes)
                if (alloc2.Allocate(s, s, out var r))
                    rectsBL.Add(r);

            // They should differ (at least in ordering)
            Assert.That(rectsBSF.Count, Is.EqualTo(rectsBL.Count));
            bool same = true;
            for (int i = 0; i < rectsBSF.Count; i++)
                if (!rectsBSF[i].Equals(rectsBL[i]))
                    same = false;
            Assert.That(same, Is.False, "Different heuristics should produce different placements");
        }

        [Test]
        public void FullPage_ReturnsFalse()
        {
            var alloc = new MaxRectsAllocator();
            alloc.Reset(100, 100);
            alloc.Allocate(100, 100, out _);
            Assert.That(alloc.Allocate(1, 1, out _), Is.False);
        }

        [Test]
        public void AreaTracking_Accurate()
        {
            var alloc = new MaxRectsAllocator();
            alloc.Reset(200, 200);
            alloc.Allocate(50, 50, out var r1);
            alloc.Allocate(70, 30, out var r2);
            long expected = 50 * 50 + 70 * 30;
            Assert.That(alloc.AllocatedArea, Is.EqualTo(expected));

            alloc.Free(r1);
            expected -= 50 * 50;
            Assert.That(alloc.AllocatedArea, Is.EqualTo(expected));
        }
    }
}