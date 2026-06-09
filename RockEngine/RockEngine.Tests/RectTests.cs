using NUnit.Framework;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Tests.Atlasing
{
    [TestFixture]
    public class RectTests
    {
        [Test]
        public void Default_InitializesToZero()
        {
            Rect r = default;
            Assert.That(r.X, Is.EqualTo(0));
            Assert.That(r.Y, Is.EqualTo(0));
            Assert.That(r.Width, Is.EqualTo(0));
            Assert.That(r.Height, Is.EqualTo(0));
            Assert.That(r.Area, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_SetsProperties()
        {
            var r = new Rect(10, 20, 30, 40);
            Assert.That(r.X, Is.EqualTo(10));
            Assert.That(r.Y, Is.EqualTo(20));
            Assert.That(r.Width, Is.EqualTo(30));
            Assert.That(r.Height, Is.EqualTo(40));
            Assert.That(r.Right, Is.EqualTo(40));
            Assert.That(r.Bottom, Is.EqualTo(60));
            Assert.That(r.Area, Is.EqualTo(1200));
        }

        [Test]
        public void Contains_SameRect_ReturnsTrue()
        {
            var a = new Rect(0, 0, 10, 10);
            Assert.That(a.Contains(a), Is.True);
        }

        [Test]
        public void Contains_InnerRect_ReturnsTrue()
        {
            var a = new Rect(0, 0, 100, 100);
            var b = new Rect(10, 10, 20, 20);
            Assert.That(a.Contains(b), Is.True);
        }

        [Test]
        public void Contains_OuterRect_ReturnsFalse()
        {
            var a = new Rect(10, 10, 10, 10);
            var b = new Rect(0, 0, 100, 100);
            Assert.That(a.Contains(b), Is.False);
        }

        [Test]
        public void Contains_TouchingEdge_ReturnsTrue()
        {
            var a = new Rect(0, 0, 10, 10);
            var b = new Rect(0, 0, 10, 5);
            Assert.That(a.Contains(b), Is.True);
            var c = new Rect(5, 5, 5, 5);
            Assert.That(a.Contains(c), Is.True);
        }
    }
}