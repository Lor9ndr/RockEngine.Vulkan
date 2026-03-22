using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Vulkan;

namespace RockEngine.Tests.Buffers
{
    [TestFixture]
    public class UniformBufferTests : TestBase
    {
        private VulkanContext _context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _context = GlobalTestSetup.VulkanContext;
        }

        [Test]
        public void Create_ShouldSucceed()
        {
            ulong size = 256;
            var uniformBuffer = new UniformBuffer(_context, size);

            Assert.That(uniformBuffer.Size, Is.EqualTo(size));
            Assert.That(uniformBuffer.AlignedSize, Is.GreaterThanOrEqualTo(size));
            Assert.That(uniformBuffer.IsDynamic, Is.False);
            Assert.That(uniformBuffer.Buffer, Is.Not.Null);
            Assert.That(uniformBuffer.Buffer.Size, Is.GreaterThanOrEqualTo(size));

            uniformBuffer.Dispose();
        }

        [Test]
        public void CreateDynamic_ShouldSetFlag()
        {
            var uniformBuffer = new UniformBuffer(_context, 256, true);
            Assert.That(uniformBuffer.IsDynamic, Is.True);
            uniformBuffer.Dispose();
        }

        [Test]
        public void Update_ShouldWriteData()
        {
            var uniformBuffer = new UniformBuffer(_context, 256);
            var testData = new Vector4(1.0f, 2.0f, 3.0f, 4.0f);

            uniformBuffer.Update(testData);

            // Read back using MapMemory
            using (var mapped = uniformBuffer.Buffer.MapMemory())
            {
                var dataSpan = mapped.GetSpan<Vector4>();
                Assert.That(dataSpan[0], Is.EqualTo(testData));
            }

            uniformBuffer.Dispose();
        }

        [Test]
        public void Update_WithArray_ShouldWriteMultiple()
        {
            using var uniformBuffer = new UniformBuffer(_context, 256);
            var testArray = new Vector4[]
            {
                new Vector4(1, 2, 3, 4),
                new Vector4(5, 6, 7, 8)
            };

            uniformBuffer.Update(testArray);

            using(var mapped = uniformBuffer.Buffer.MapMemory())
            {
                var dataSpan = mapped.GetSpan<Vector4>();
                for (int i = 0; i < testArray.Length; i++)
                {
                    Assert.That(dataSpan[i], Is.EqualTo(testArray[i]));
                }
            }
        }

        [Test]
        public void Update_WithOffsetAndSize_ShouldWriteAtCorrectPosition()
        {
            var uniformBuffer = new UniformBuffer(_context, 128);
            var first = new Vector4(1, 2, 3, 4);
            var second = new Vector4(5, 6, 7, 8);
            uint elementSize = (uint)Unsafe.SizeOf<Vector4>();
            uint offset = elementSize;

            uniformBuffer.Update(first);
            uniformBuffer.Update(second, elementSize, offset);

            using(var mapped = uniformBuffer.Buffer.MapMemory())
            {
                var dataSpan = mapped.GetSpan<Vector4>();
                Assert.That(dataSpan[0], Is.EqualTo(first));
                Assert.That(dataSpan[1], Is.EqualTo(second));
            }

            uniformBuffer.Dispose();
        }

        [Test]
        public void Update_WithDataLargerThanRequestedSize_ShouldThrow()
        {
            var uniformBuffer = new UniformBuffer(_context, 32);
            var testData = new byte[33];
            Assert.Throws<ArgumentException>(() => uniformBuffer.Update(testData));
            uniformBuffer.Dispose();
        }

        [Test]
        public void Update_WithOffsetPlusSizeExceedingRequestedSize_ShouldThrow()
        {
            var uniformBuffer = new UniformBuffer(_context, 32);
            var testData = new Vector4(1, 2, 3, 4); // size 16 bytes
            Assert.Throws<ArgumentException>(() => uniformBuffer.Update(testData, 16, 24));
            uniformBuffer.Dispose();
        }

        [Test]
        public void FlushBuffer_ShouldWork()
        {
            var uniformBuffer = new UniformBuffer(_context, 256);
            var testData = new Vector4(1, 2, 3, 4);

            uniformBuffer.Update(testData);
            uniformBuffer.FlushBuffer(); // Should not throw

            uniformBuffer.Dispose();
        }

        [Test]
        public void Dispose_ShouldReleaseResources()
        {
            var uniformBuffer = new UniformBuffer(_context, 256);
            uniformBuffer.Dispose();

            // Disposing twice should be safe
            uniformBuffer.Dispose();

            // After disposal, the underlying VkBuffer is disposed; accessing properties is allowed,
            // but any Vulkan operation would fail (not tested here).
        }
    }
}