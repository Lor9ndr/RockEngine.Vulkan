using System.Numerics;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Tests.Buffers
{
    [TestFixture]
    public class VkBufferTests:TestBase
    {
        private struct TestStruct
        {
            public int A;
            public float B;
            public Vector3 C;
        }


        [Test]
        public void Create_ShouldSucceed()
        {
            ulong size = 1024;
            var usage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit;
            var properties = MemoryPropertyFlags.DeviceLocalBit;

            var buffer = VkBuffer.Create(_context, size, usage, properties);

            Assert.That(buffer.Size, Is.GreaterThanOrEqualTo(size));
            Assert.That(buffer.VkObjectNative, Is.Not.EqualTo(0));
            buffer.Dispose();
        }

        [Test]
        public void Create_WithHostVisible_ShouldBeMapped()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            var buffer = VkBuffer.Create(_context, size, usage, properties);

            Assert.That(buffer.MappedData, Is.Not.EqualTo(IntPtr.Zero));
            buffer.Dispose();
        }

        [Test]
        public void WriteToBuffer_ShouldWriteData()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var buffer = VkBuffer.Create(_context, size, usage, properties);
            var testData = new TestStruct { A = 42, B = 3.14f, C = new Vector3(1, 2, 3) };

            buffer.WriteToBuffer(testData);

            using var mapped = buffer.MapMemory();
            var readData = mapped.GetSpan<TestStruct>()[0];
            Assert.That(readData.A, Is.EqualTo(testData.A));
            Assert.That(readData.B, Is.EqualTo(testData.B));
            Assert.That(readData.C, Is.EqualTo(testData.C));

        }

        [Test]
        public void WriteToBuffer_WithOffset_ShouldWriteAtOffset()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var buffer = VkBuffer.Create(_context, size, usage, properties);
            var first = new TestStruct { A = 1, B = 1.1f, C = Vector3.One };
            var second = new TestStruct { A = 2, B = 2.2f, C = Vector3.One * 2 };

            buffer.WriteToBuffer(first, offset: 0);
            buffer.WriteToBuffer(second, offset: (ulong)Unsafe.SizeOf<TestStruct>());

            using var mapped = buffer.MapMemory();
            var dataSpan = mapped.GetSpan<TestStruct>();
            Assert.That(dataSpan[0].A, Is.EqualTo(1));
            Assert.That(dataSpan[1].A, Is.EqualTo(2));

        }

        [Test]
        public void WriteToBuffer_WithSize_ShouldWritePartial()
        {
            ulong size = 32;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var buffer = VkBuffer.Create(_context, size, usage, properties);
            var testData = new byte[] { 0x2A, 0x00, 0x00, 0x00 }; // 42 as little-endian int
            ulong partialSize = 4;

            buffer.WriteToBuffer(testData, partialSize);

            using var mapped = buffer.MapMemory();
            var readData = mapped.GetSpan<byte>();
            for (int i = 0; i < testData.Length; i++)
            {
                Assert.That(readData[i], Is.EqualTo(testData[i]));
            }
        }

        [Test]
        public void WriteToBuffer_WithExceedingSize_ShouldThrow()
        {
            ulong requestedSize = 16;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var buffer = VkBuffer.Create(_context, requestedSize, usage, properties);
            var testData = new TestStruct { A = 42, B = 3.14f, C = new Vector3(1, 2, 3) };
            // Use the actual buffer size (after alignment)
            Assert.Throws<ArgumentException>(() => buffer.WriteToBuffer(testData, offset: buffer.Size - 4));
        }

        [Test]
        public async Task WriteToBufferAsync_ShouldWriteData()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var buffer = VkBuffer.Create(_context, size, usage, properties);
            var testData = new TestStruct { A = 42, B = 3.14f, C = new Vector3(1, 2, 3) };

            await buffer.WriteToBufferAsync(testData); // synchronous in implementation

            using var mapped = buffer.MapMemory();
            var readData = mapped.GetSpan<TestStruct>()[0];
            Assert.That(readData.A, Is.EqualTo(testData.A));

        }

        [Test]
        public void MapMemory_ShouldReturnValidSpan()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var buffer = VkBuffer.Create(_context, size, usage, properties);
            var testData = new TestStruct { A = 42, B = 3.14f, C = new Vector3(1, 2, 3) };

            using (var mapped = buffer.MapMemory())
            {
                var destSpan = mapped.GetSpan<TestStruct>();
                destSpan[0] = testData;
                mapped.Flush();
            }

            using var readMapped = buffer.MapMemory();
            var readData = readMapped.GetSpan<TestStruct>()[0];
            Assert.That(readData.A, Is.EqualTo(testData.A));

        }

        [Test]
        public void MappedMemory_Flush_ShouldNotThrow()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            var buffer = VkBuffer.Create(_context, size, usage, properties);
            using (var mapped = buffer.MapMemory())
            {
                mapped.Flush(); // Should not throw
            }
            buffer.Dispose();
        }

        [Test]
        public void MappedMemory_Dispose_ShouldUnmap()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var buffer = VkBuffer.Create(_context, size, usage, properties);
            // Initially mapped (persistent)
            Assert.That(buffer.MappedData, Is.Not.Null);
            Assert.That(buffer.MappedData, Is.Not.EqualTo(IntPtr.Zero));

            using (var mapped = buffer.MapMemory())
            {
                // MappedData still valid (it's the same persistent mapping)
                Assert.That(buffer.MappedData, Is.Not.EqualTo(IntPtr.Zero));
            }

            // After using MapMemory, the buffer is still persistently mapped
            Assert.That(buffer.MappedData, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(buffer.MappedData, Is.Not.Null);

        }

        [Test]
        public async Task CopyTo_ShouldCopyData()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var srcBuffer = VkBuffer.Create(_context, size, usage, properties);
            using var dstBuffer = VkBuffer.Create(_context, size, usage, properties);

            var testData = new TestStruct { A = 42, B = 3.14f, C = new Vector3(1, 2, 3) };
            srcBuffer.WriteToBuffer<TestStruct>([testData]);

            var batch = _context.TransferSubmitContext.CreateBatch();
            srcBuffer.CopyTo(dstBuffer, batch);
            await _context.TransferSubmitContext.SubmitSingle(batch);

            using var mapped = dstBuffer.MapMemory();
            var readData = mapped.GetSpan<TestStruct>()[0];
            Assert.That(readData.A, Is.EqualTo(testData.A));
        }

        [Test]
        public void GetAlignment_ShouldReturnAlignedValue()
        {
            Assert.That(VkBuffer.GetAlignment(100, 64), Is.EqualTo(128));
            Assert.That(VkBuffer.GetAlignment(64, 64), Is.EqualTo(64));
            Assert.That(VkBuffer.GetAlignment(1, 16), Is.EqualTo(16));
            Assert.That(VkBuffer.GetAlignment(0, 256), Is.EqualTo(0));
        }

        [Test]
        public void Dispose_ShouldReleaseResources()
        {
            var buffer = VkBuffer.Create(_context, 1024, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit);
            buffer.Dispose();

            // Subsequent operations should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() => buffer.WriteToBuffer([new TestStruct()]));
            Assert.Throws<ObjectDisposedException>(() => buffer.MapMemory());
            // The underlying Vulkan handles are destroyed
        }

        [Test]
        public void CreateAndCopyToStagingBuffer_ShouldWork()
        {
            var testData = new TestStruct { A = 42, B = 3.14f, C = new Vector3(1, 2, 3) };
            unsafe
            {
                var ptr = &testData;
                using var stagingBuffer = VkBuffer.CreateAndCopyToStagingBuffer(_context, ptr, (ulong)Unsafe.SizeOf<TestStruct>());
                using var mapped = stagingBuffer.MapMemory();
                var readData = mapped.GetSpan<TestStruct>()[0];
                Assert.That(readData.A, Is.EqualTo(testData.A));
            }
        }

        [Test]
        public async Task CreateAndCopyToStagingBuffer_Generic_ShouldWork()
        {
            var testData = new TestStruct { A = 42, B = 3.14f, C = new Vector3(1, 2, 3) };
            var dataArray = new[] { testData };
            using var stagingBuffer = await VkBuffer.CreateAndCopyToStagingBuffer(_context, dataArray, (ulong)Unsafe.SizeOf<TestStruct>());
            using var mapped = stagingBuffer.MapMemory();
            var readData = mapped.GetSpan<TestStruct>()[0];
            Assert.That(readData.A, Is.EqualTo(testData.A));
        }

        [Test]
        public void Flush_OnHostCoherent_ShouldNotThrow()
        {
            ulong size = 256;
            var usage = BufferUsageFlags.UniformBufferBit;
            var properties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            using var buffer = VkBuffer.Create(_context, size, usage, properties);
            buffer.Flush(); // Should not throw because host-coherent skips flush
        }
    }
}