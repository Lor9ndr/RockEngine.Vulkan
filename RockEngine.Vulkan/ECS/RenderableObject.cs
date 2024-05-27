using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System;
using System.Threading.Tasks;

namespace RockEngine.Vulkan.ECS
{
    internal class RenderableObject : IDisposable
    {
        public Vertex[] Vertices { get; private set; }
        public uint[]? Indicies { get; private set; }

        private BufferWrapper _vertexBuffer;
        private BufferWrapper? _indexBuffer;
        private bool _isReady = false;
        private static readonly Mutex _queueMutex = new Mutex();

        public RenderableObject(Vertex[] vertices, uint[]? indicies = null)
        {
            Vertices = vertices;
            Indicies = indicies;
        }

        public async Task CreateBuffersAsync(VulkanContext context)
        {
            ulong vertexBufferSize = (ulong)(Vertices.Length * Vertex.Size);

            // Create Staging Buffer for Vertex Data
           using BufferWrapper stagingBuffer = new VulkanBufferBuilder(context.Api, context.Device)
                .Configure(SharingMode.Exclusive, vertexBufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
                .Build();

            await stagingBuffer.SendDataAsync(Vertices).ConfigureAwait(false);

            // Create Vertex Buffer with Device Local Memory
            _vertexBuffer = new VulkanBufferBuilder(context.Api, context.Device)
                .Configure(SharingMode.Exclusive, vertexBufferSize, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit)
                .Build();

            // Copy Data from Staging Buffer to Vertex Buffer
            stagingBuffer.CopyBuffer(context, _vertexBuffer, vertexBufferSize);


            if (Indicies != null)
            {
                ulong indexBufferSize = (ulong)(Indicies.Length * sizeof(uint));

                // Create Staging Buffer for Index Data
                BufferWrapper stagingIndexBuffer = new VulkanBufferBuilder(context.Api, context.Device)
                    .Configure(SharingMode.Exclusive, indexBufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
                    .Build();

                await stagingIndexBuffer.SendDataAsync(Indicies).ConfigureAwait(false);

                // Create Index Buffer with Device Local Memory
                _indexBuffer = new VulkanBufferBuilder(context.Api, context.Device)
                    .Configure(SharingMode.Exclusive, indexBufferSize, BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit)
                    .Build();

                // Copy Data from Staging Buffer to Index Buffer
                stagingIndexBuffer.CopyBuffer(context, _indexBuffer, indexBufferSize);
               

                stagingIndexBuffer.Dispose();
            }
            _isReady = true;
        }

        

        public void BindBuffers(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            var buffer = _vertexBuffer.Buffer;
            ulong offset = 0;
            context.Api.CmdBindVertexBuffers(commandBuffer.CommandBuffer, 0, 1, in buffer, in offset);

            if (_indexBuffer != null)
            {
                context.Api.CmdBindIndexBuffer(commandBuffer.CommandBuffer, _indexBuffer.Buffer, 0, IndexType.Uint32);
            }
        }

        public void Draw(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            if (!_isReady)
            {
                return;
            }
            _vertexBuffer.Bind(commandBuffer);
            if (_indexBuffer != null)
            {
                context.Api.CmdDrawIndexed(commandBuffer.CommandBuffer, (uint)Indicies!.Length, 1, 0, 0, 0);
            }
            else
            {
                context.Api.CmdDraw(commandBuffer.CommandBuffer, (uint)Vertices.Length, 1, 0, 0);
            }
        }

        public void Dispose()
        {
            _vertexBuffer.Dispose();
            _indexBuffer?.Dispose();
            _isReady = false;
        }
    }
}