using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.ECS.Components
{
    public class Mesh : Component, IDisposable
    {
        private Vertex[] _vertices;
        private uint[]? _indices;
        private Material _material;

        public VkBuffer VertexBuffer;
        public VkBuffer? IndexBuffer;

        public bool HasIndices => IndicesCount > 0;
        public uint IndicesCount { get; private set; }
        public uint VerticesCount { get; private set;}

        public Material Material { get => _material; set => _material = value; }

        public Mesh()
        {
        }

        public void SetMeshData(Vertex[] vertices, uint[]? indices = null)
        {
            _vertices = vertices;
            _indices = indices;
            VerticesCount = (uint)_vertices.Length;
            IndicesCount = (uint)(indices?.Length ?? 0);
        }


        public override async ValueTask OnStart(Renderer renderer)
        {
            var submitContext = renderer.SubmitContext;
            var batch = submitContext.CreateBatch();
            batch.CommandBuffer.LabelObject("Mesh cmd");
            var context = VulkanContext.GetCurrent();
            // Create device-local buffers
            VertexBuffer = VkBuffer.Create(
                context,
                (ulong)(Unsafe.SizeOf<Vertex>() * _vertices.Length),
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit);

            // Stage vertex data
            batch.StageToBuffer(
                _vertices.AsSpan(),
                VertexBuffer,
                0,
                (ulong)(Unsafe.SizeOf<Vertex>() * _vertices.Length));

            if (HasIndices)
            {
                IndexBuffer = VkBuffer.Create(
                    context,
                    (ulong)(Unsafe.SizeOf<uint>() * _indices!.Length),
                    BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit);

                // Stage index data
                batch.StageToBuffer(
                    _indices.AsSpan(),
                    IndexBuffer,
                    0,
                    (ulong)(Unsafe.SizeOf<uint>() * _indices.Length));
            }

            batch.Submit();
            renderer.Draw(this);
        }

        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer?.Dispose();
        }
    }
}
