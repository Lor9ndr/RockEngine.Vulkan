using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.ECS.Components
{
    public class Mesh : Component, IDisposable
    {
        public Vertex[] Vertices;
        public uint[]? Indices;
        public Material Material;

        public VkBuffer VertexBuffer;
        public VkBuffer? IndexBuffer;

        public bool HasIndices => Indices?.Length > 0;

        public Mesh()
        {
        }

        public void SetMeshData(Vertex[] vertices, uint[]? indices = null)
        {
            Vertices = vertices;
            Indices = indices;
        }


        public override async ValueTask OnStart(Renderer renderer)
        {
            var submitContext = renderer.SubmitContext;
            var batch = submitContext.CreateBatch();
            var context = VulkanContext.GetCurrent();
            // Create device-local buffers
            VertexBuffer = VkBuffer.Create(
                context,
                (ulong)(Unsafe.SizeOf<Vertex>() * Vertices.Length),
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit);

            // Stage vertex data
            batch.StageToBuffer(
                Vertices,
                VertexBuffer,
                0,
                (ulong)(Unsafe.SizeOf<Vertex>() * Vertices.Length));

            if (HasIndices)
            {
                IndexBuffer = VkBuffer.Create(
                    context,
                    (ulong)(Unsafe.SizeOf<uint>() * Indices!.Length),
                    BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit);

                // Stage index data
                batch.StageToBuffer(
                    Indices,
                    IndexBuffer,
                    0,
                    (ulong)(Unsafe.SizeOf<uint>() * Indices.Length));
            }

            batch.Submit();
            await submitContext.FlushAsync();
            renderer.Draw(this);
        }

        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer?.Dispose();
        }
    }
}
