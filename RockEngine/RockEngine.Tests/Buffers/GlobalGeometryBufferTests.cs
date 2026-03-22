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
    public class GlobalGeometryBufferTests : TestBase
    {
        private VulkanContext _context;
        private GlobalGeometryBuffer _geometryBuffer;

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

        [SetUp]
        public void SetUp()
        {
            // Create a fresh buffer for each test
            _geometryBuffer = new GlobalGeometryBuffer(_context, 1024, 1024);
        }

        [TearDown]
        public async Task TearDown()
        {
            await WaitForIdle(_context.TransferSubmitContext);
            await WaitForIdle(_context.GraphicsSubmitContext);
            _geometryBuffer?.Dispose();
        }

        private SubmitOperation WaitForIdle(SubmitContext submitContext) => submitContext.Submit();

        [Test]
        public async Task AddMesh_ShouldAllocateSpace()
        {
            var vertices = new SimpleVertex[]
            {
                new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero),
                new SimpleVertex(Vector3.One, Vector3.UnitZ, Vector2.One)
            };
            var indices = new uint[] { 0, 1 };
            var meshId = Guid.NewGuid();

            var allocation = await _geometryBuffer.AddMeshAsync(meshId, vertices, indices);

            Assert.That(allocation.MeshID, Is.EqualTo(meshId));
            Assert.That(allocation.VertexCount, Is.EqualTo(vertices.Length));
            Assert.That(allocation.IndexCount, Is.EqualTo(indices.Length));
            Assert.That(allocation.VertexSize, Is.GreaterThan(0));
            Assert.That(allocation.IndexSize, Is.GreaterThan(0));

            var stride = _geometryBuffer.GetVertexStride(meshId);
            Assert.That(stride, Is.EqualTo((uint)Unsafe.SizeOf<SimpleVertex>()));

            var binding = _geometryBuffer.GetVertexBindingDescription(meshId);
            Assert.That(binding.Stride, Is.EqualTo(stride));

            var attributes = _geometryBuffer.GetVertexAttributeDescriptions(meshId);
            Assert.That(attributes.Length, Is.EqualTo(3));

            _geometryBuffer.RemoveMesh(meshId);
        }

        [Test]
        public async Task RemoveMesh_ShouldFreeSpace()
        {
            var vertices = new SimpleVertex[] { new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) };
            var indices = new uint[] { 0 };
            var meshId = Guid.NewGuid();

            var allocation = await _geometryBuffer.AddMeshAsync(meshId, vertices, indices);
            var initialAllocation = allocation;

            _geometryBuffer.RemoveMesh(meshId);

            Assert.That(() => _geometryBuffer.GetVertexStride(meshId), Throws.TypeOf<KeyNotFoundException>());

            // Add another mesh and verify it uses the freed space (approximate)
            var meshId2 = Guid.NewGuid();
            var allocation2 = await _geometryBuffer.AddMeshAsync(meshId2, vertices, indices);
            Assert.That(allocation2.VertexOffset, Is.EqualTo(initialAllocation.VertexOffset));

            _geometryBuffer.RemoveMesh(meshId2);
        }

        [Test]
        public async Task ForEachMesh_ShouldIterate()
        {
            var meshIds = new List<Guid>();
            for (int i = 0; i < 3; i++)
            {
                var id = Guid.NewGuid();
                meshIds.Add(id);
                await _geometryBuffer.AddMeshAsync(id,
                    new SimpleVertex[] { new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                    new uint[] { 0 });
            }

            var processedIds = new List<Guid>();
            _geometryBuffer.ForEachMesh((id, alloc, format) => processedIds.Add(id));

            Assert.That(processedIds, Is.EquivalentTo(meshIds));

            foreach (var id in meshIds)
            {
                _geometryBuffer.RemoveMesh(id);
            }
        }

        [Test]
        public async Task WithMeshFormat_ShouldExecuteAction()
        {
            var meshId = Guid.NewGuid();
            await _geometryBuffer.AddMeshAsync(meshId,
                new SimpleVertex[] { new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                new uint[] { 0 });

            bool called = false;
            _geometryBuffer.WithMeshFormat(meshId, (binding, attributes) =>
            {
                called = true;
                Assert.That(binding.Stride, Is.EqualTo((uint)Unsafe.SizeOf<SimpleVertex>()));
                Assert.That(attributes.Length, Is.EqualTo(3));
                return 42;
            });

            Assert.That(called, Is.True);
            _geometryBuffer.RemoveMesh(meshId);
        }

        [Test]
        public async Task Defragment_ShouldCompactData()
        {
            var vertices = new SimpleVertex[]
            {
                new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero),
                new SimpleVertex(Vector3.One, Vector3.UnitZ, Vector2.One)
            };
            var indices = new uint[] { 0, 1 };
            var meshIds = new List<Guid>();

            // Add 5 meshes
            for (int i = 0; i < 5; i++)
            {
                var id = Guid.NewGuid();
                meshIds.Add(id);
                await _geometryBuffer.AddMeshAsync(id, vertices, indices);
            }

            // Remove every other mesh to create fragmentation
            for (int i = 0; i < meshIds.Count; i += 2)
            {
                _geometryBuffer.RemoveMesh(meshIds[i]);
            }

            await _geometryBuffer.DefragmentAsync();
            await WaitForIdle(_context.TransferSubmitContext);
            await WaitForIdle(_context.GraphicsSubmitContext);

            // Add a new mesh
            var newMeshId = Guid.NewGuid();
            var newAllocation = await _geometryBuffer.AddMeshAsync(newMeshId, vertices, indices);

            // After defragmentation, all remaining meshes are compacted to the start.
            // Compute the total used vertex size (sum of vertex sizes of remaining meshes).
            var remainingIds = meshIds.Where((_, i) => i % 2 != 0).ToList(); // indices 1 and 3
            ulong totalUsedVertexSize = 0;
            foreach (var id in remainingIds)
            {
                var alloc = _geometryBuffer.GetMeshAllocation(id);
                totalUsedVertexSize += alloc.VertexSize;
            }

            // The new mesh should be placed exactly at the end of the used area (due to uniform stride/size).
            Assert.That(newAllocation.VertexOffset, Is.EqualTo(totalUsedVertexSize));
            
        }

        [Test]
        public async Task Bind_ShouldNotThrow()
        {
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            {
                Assert.That(() => _geometryBuffer.Bind(batch), Throws.Nothing);
            }
            batch.Submit();
            await _context.GraphicsSubmitContext.Submit();
        }

        [Test]
        public async Task Dispose_ShouldReleaseResources()
        {
            var localBuffer = new GlobalGeometryBuffer(_context, 1024, 1024);
            var meshId = Guid.NewGuid();
            await localBuffer.AddMeshAsync(meshId,
                new SimpleVertex[] { new SimpleVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                new uint[] { 0 });

            localBuffer.Dispose();

            Assert.That(() => localBuffer.GetVertexStride(meshId), Throws.TypeOf<ObjectDisposedException>());
        }
    }
}