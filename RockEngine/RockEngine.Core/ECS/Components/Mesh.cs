using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

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
            await CreateVertexBufferAsync(RenderingContext.GetCurrent(), renderer.CommandPool);
            await CreateIndexBufferAsync(RenderingContext.GetCurrent(), renderer.CommandPool);
        }
      
       
        private async ValueTask CreateVertexBufferAsync(RenderingContext context, VkCommandPool commandPool)
        {
            ulong vertexBufferSize = (ulong)(Vertices.Length * Vertex.Size);
            VertexBuffer = await CreateDeviceLocalBufferAsync(context,vertexBufferSize, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit, Vertices, commandPool);
        }

        private async ValueTask CreateIndexBufferAsync(RenderingContext context, VkCommandPool commandPool)
        {
            if (Indices != null)
            {
                ulong indexBufferSize = (ulong)(Indices.Length * sizeof(uint));
                IndexBuffer = await CreateDeviceLocalBufferAsync(context, indexBufferSize, BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit, Indices, commandPool);
            }
        }

        private static async ValueTask<VkBuffer> CreateDeviceLocalBufferAsync<T>(RenderingContext context, ulong bufferSize, BufferUsageFlags usage, T[] data, VkCommandPool commandPool) where T : unmanaged
        {
            using var stagingBuffer = await VkBuffer.CreateAndCopyToStagingBuffer(context,data, bufferSize);

            var deviceLocalBuffer = VkBuffer.Create(context, bufferSize, usage, MemoryPropertyFlags.DeviceLocalBit);

            stagingBuffer.CopyTo(deviceLocalBuffer, commandPool);


            return deviceLocalBuffer;
        }


        public override ValueTask Update(Renderer renderer)
        {
            renderer.Draw(this);
            return default;
        }

        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer?.Dispose();
        }
    }
}
