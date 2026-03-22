using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using RockEngine.Core;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Tests.Buffers
{
    [TestFixture]
    public class StorageBufferTests : TestBase
    {
        private VulkanContext _context;

        private struct SimpleVertex : IVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 TexCoord;

            public SimpleVertex(Vector3 position, Vector3 normal, Vector2 texCoord)
            {
                Position = position;
                Normal = normal;
                TexCoord = texCoord;
            }

            public static VertexInputBindingDescription GetBindingDescription() => new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)Unsafe.SizeOf<SimpleVertex>(),
                InputRate = VertexInputRate.Vertex
            };

            public static VertexInputAttributeDescription[] GetAttributeDescriptions() => new[]
            {
                new VertexInputAttributeDescription
                {
                    Binding = 0,
                    Location = 0,
                    Format = Format.R32G32B32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<SimpleVertex>(nameof(Position))
                },
                new VertexInputAttributeDescription
                {
                    Binding = 0,
                    Location = 1,
                    Format = Format.R32G32B32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<SimpleVertex>(nameof(Normal))
                },
                new VertexInputAttributeDescription
                {
                    Binding = 0,
                    Location = 2,
                    Format = Format.R32G32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<SimpleVertex>(nameof(TexCoord))
                }
            };
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _context = GlobalTestSetup.VulkanContext;
        }

        [Test]
        public async Task Create_ShouldSucceed()
        {
            ulong capacity = 100;
            var storageBuffer = new StorageBuffer<SimpleVertex>(_context, capacity);

            Assert.That(storageBuffer.Capacity, Is.EqualTo(capacity));
            Assert.That(storageBuffer.Stride, Is.GreaterThanOrEqualTo((ulong)Unsafe.SizeOf<SimpleVertex>()));
            // Actual buffer size may be larger due to alignment
            Assert.That(storageBuffer.Buffer.Size, Is.GreaterThanOrEqualTo(capacity * storageBuffer.Stride));

            storageBuffer.Dispose();
        }

        [Test]
        public async Task StageData_ShouldWriteData()
        {
            var vertices = new[]
            {
                new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero),
                new SimpleVertex(Vector3.One, Vector3.UnitZ, Vector2.One),
                new SimpleVertex(new Vector3(0,1,0), Vector3.UnitZ, Vector2.UnitX)
            };
            var storageBuffer = new StorageBuffer<SimpleVertex>(_context, 10);

            var batch = _context.TransferSubmitContext.CreateBatch();
            storageBuffer.StageData(batch, vertices);
            await _context.TransferSubmitContext.SubmitSingle(batch);

            Assert.Pass(); // No exception means success
            storageBuffer.Dispose();
        }

        [Test]
        public async Task StageData_ShouldThrowIfExceedsCapacity()
        {
            ulong capacity = 2;
            var vertices = new SimpleVertex[]
            {
                new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero),
                new SimpleVertex(Vector3.One, Vector3.UnitZ, Vector2.One),
                new SimpleVertex(new Vector3(2,2,2), Vector3.UnitZ, Vector2.Zero)
            };
            var storageBuffer = new StorageBuffer<SimpleVertex>(_context, capacity);

            var batch = _context.TransferSubmitContext.CreateBatch();
            Assert.That(() => storageBuffer.StageData(batch, vertices), Throws.TypeOf<ArgumentOutOfRangeException>());

            storageBuffer.Dispose();
        }

        [Test]
        public async Task Resize_ShouldPreserveData()
        {
            var initialData = new[]
            {
                new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero),
                new SimpleVertex(Vector3.One, Vector3.UnitZ, Vector2.One)
            };
            var storageBuffer = new StorageBuffer<SimpleVertex>(_context, 2);

            // Upload initial data
            var batch = _context.TransferSubmitContext.CreateBatch();
            storageBuffer.StageData(batch, initialData);
            await _context.TransferSubmitContext.SubmitSingle(batch);

            // Resize to larger capacity
            ulong newCapacity = 5;
            batch = _context.GraphicsSubmitContext.CreateBatch();
            storageBuffer.Resize(newCapacity, batch);
            await _context.GraphicsSubmitContext.SubmitSingle(batch);

            Assert.That(storageBuffer.Capacity, Is.EqualTo(newCapacity));
            Assert.That(storageBuffer.Buffer.Size, Is.GreaterThanOrEqualTo(newCapacity * storageBuffer.Stride));

            storageBuffer.Dispose();
        }

        [Test]
        public async Task Resize_ShouldWorkWhenCapacityDecreases()
        {
            ulong initialCapacity = 10;
            var storageBuffer = new StorageBuffer<SimpleVertex>(_context, initialCapacity);
            ulong newCapacity = 5;

            var batch = _context.GraphicsSubmitContext.CreateBatch();
            storageBuffer.Resize(newCapacity, batch);
            await _context.GraphicsSubmitContext.SubmitSingle(batch);

            Assert.That(storageBuffer.Capacity, Is.EqualTo(newCapacity));
            storageBuffer.Dispose();
        }

        [Test]
        public async Task Dispose_ShouldReleaseResources()
        {
            var storageBuffer = new StorageBuffer<SimpleVertex>(_context, 1);
            storageBuffer.Dispose();

            // After disposal, accessing Buffer should still be safe (returns null)
            Assert.That(() => storageBuffer.Buffer, Throws.Nothing);
            // Using any other method that touches the buffer should throw ObjectDisposedException
            Assert.That(() => storageBuffer.StageData(_context.TransferSubmitContext.CreateBatch(), new SimpleVertex[1]),
                Throws.TypeOf<ObjectDisposedException>());
        }
    }
}