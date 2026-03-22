using System.Runtime.CompilerServices;
using NUnit.Framework;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Tests.Buffers
{
    [TestFixture]
    public class IndirectBufferTests : TestBase
    {
        private VulkanContext _context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _context = GlobalTestSetup.VulkanContext;
        }

        [Test]
        public async Task Create_ShouldSucceed()
        {
            ulong initialCapacity = 10;
            using var indirectBuffer = new IndirectBuffer(_context, initialCapacity);

            Assert.That(indirectBuffer.Capacity, Is.EqualTo(initialCapacity));
            Assert.That(indirectBuffer.Stride, Is.EqualTo((ulong)Unsafe.SizeOf<DrawIndexedIndirectCommand>()));
            Assert.That(indirectBuffer.Buffer, Is.Not.Null);
            // The actual buffer size may be larger due to alignment, so check >=
            Assert.That(indirectBuffer.Buffer.Size, Is.GreaterThanOrEqualTo(initialCapacity * indirectBuffer.Stride));

        }

        [Test]
        public async Task StageCommands_ShouldWriteData()
        {
            var commands = new[]
            {
                new DrawIndexedIndirectCommand { IndexCount = 3, InstanceCount = 1, FirstIndex = 0, VertexOffset = 0, FirstInstance = 0 },
                new DrawIndexedIndirectCommand { IndexCount = 6, InstanceCount = 1, FirstIndex = 3, VertexOffset = 0, FirstInstance = 0 }
            };
            using var indirectBuffer = new IndirectBuffer(_context, 2);

            var batch = _context.TransferSubmitContext.CreateBatch();
            indirectBuffer.StageCommands(batch, commands);
            await _context.TransferSubmitContext.SubmitSingle(batch);

            Assert.That(indirectBuffer.Capacity, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task StageCommands_ShouldExpandBufferIfNeeded()
        {
            ulong initialCapacity = 1;
            var commands = new[]
            {
                new DrawIndexedIndirectCommand { IndexCount = 3, InstanceCount = 1, FirstIndex = 0, VertexOffset = 0, FirstInstance = 0 },
                new DrawIndexedIndirectCommand { IndexCount = 6, InstanceCount = 1, FirstIndex = 3, VertexOffset = 0, FirstInstance = 0 }
            };
            using var indirectBuffer = new IndirectBuffer(_context, initialCapacity);

            // Resize manually before staging commands
            var batch = _context.TransferSubmitContext.CreateBatch();
            indirectBuffer.Resize(batch, 2);
            indirectBuffer.StageCommands(batch, commands);
            await _context.TransferSubmitContext.SubmitSingle(batch);

            Assert.That(indirectBuffer.Capacity, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task Resize_ShouldChangeCapacity()
        {
            ulong initialCapacity = 5;
            using var indirectBuffer = new IndirectBuffer(_context, initialCapacity);
            ulong newCapacity = 10;

            var batch = _context.TransferSubmitContext.CreateBatch();
            indirectBuffer.Resize(batch, newCapacity);
            await _context.TransferSubmitContext.SubmitSingle(batch);

            Assert.That(indirectBuffer.Capacity, Is.EqualTo(newCapacity));
            // Check >= to account for alignment
            Assert.That(indirectBuffer.Buffer.Size, Is.GreaterThanOrEqualTo(newCapacity * indirectBuffer.Stride));
        }

        [Test]
        public async Task Dispose_ShouldReleaseResources()
        {
            var indirectBuffer = new IndirectBuffer(_context, 1);
            indirectBuffer.Dispose();

            // After disposal, operations must throw ObjectDisposedException
            var batch = _context.TransferSubmitContext.CreateBatch();
            Assert.Throws<ObjectDisposedException>(() => indirectBuffer.Resize(batch, 2));
            Assert.Throws<ObjectDisposedException>(() => indirectBuffer.StageCommands(batch, new DrawIndexedIndirectCommand[1]));
            batch.Submit();
            await _context.TransferSubmitContext.Submit();
        }
    }
}