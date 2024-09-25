using RockEngine.Core.Info;
using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.ECS.Components
{
    public class Mesh : IComponent, IRenderable, IDisposable
    {
        public Vertex[] Vertices;
        public uint[]? Indices;
        public Material Material;

        private VkBuffer _vertexBuffer;
        private VkBuffer _indexBuffer;

        public bool HasIndices => Indices?.Length > 0;

        public Mesh(Vertex[] vertices, uint[]? indices = null)
        {
            Vertices = vertices;
            Indices = indices;
        }

        public async ValueTask Init(RenderingContext context, Renderer renderer)
        {
            var setLayout = Material.Pipeline.Layout.GetSetLayout(DescriptorSetsInfo.MATERIAL_LOCATION);
            var materialSet = renderer.DescriptorPool.AllocateDescriptorSet(setLayout.DescriptorSetLayout);
            for (int i = 0; i < Material.Textures.Length; i++)
            {
                Texture? item = Material.Textures[i];
                item.UpdateSet(materialSet,setLayout.DescriptorSetLayout, (uint)i);
            }
            await CreateVertexBufferAsync(context, renderer.CommandPool);
            await CreateIndexBufferAsync(context, renderer.CommandPool);
        }
       
        private async ValueTask CreateVertexBufferAsync(RenderingContext context, VkCommandPool commandPool)
        {
            ulong vertexBufferSize = (ulong)(Vertices.Length * Vertex.Size);
            _vertexBuffer = await CreateDeviceLocalBufferAsync(context,vertexBufferSize, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit, Vertices, commandPool);
        }

        private async ValueTask CreateIndexBufferAsync(RenderingContext context, VkCommandPool commandPool)
        {
            if (Indices != null)
            {
                ulong indexBufferSize = (ulong)(Indices.Length * sizeof(uint));
                _indexBuffer = await CreateDeviceLocalBufferAsync(context, indexBufferSize, BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit, Indices, commandPool);
            }
        }

        private static async ValueTask<VkBuffer> CreateDeviceLocalBufferAsync<T>(RenderingContext context, ulong bufferSize, BufferUsageFlags usage, T[] data, VkCommandPool commandPool) where T : unmanaged
        {
            using var stagingBuffer = VkBuffer.Create(context, bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            await stagingBuffer.WriteToBufferAsync(data);

            var deviceLocalBuffer = VkBuffer.Create(context, bufferSize, usage, MemoryPropertyFlags.DeviceLocalBit);

            stagingBuffer.CopyTo(deviceLocalBuffer, commandPool);

            return deviceLocalBuffer;
        }

        public ValueTask Render(Renderer renderer)
        {
           /* renderer.UseMaterial(Material);

            if (_indexBuffer != null)
            {
                renderer.DrawMesh(_vertexBuffer, _indexBuffer, (uint)Vertices.Length, (uint)Indices!.Length);
            }
            else
            {
                renderer.DrawMesh(_vertexBuffer, (uint)Vertices.Length);
            }*/
            return default;
        }

        public void Dispose()
        {
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
        }

        public void Update()
        {
        }
    }
}
