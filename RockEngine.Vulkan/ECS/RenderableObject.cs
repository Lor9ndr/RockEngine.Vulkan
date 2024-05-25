using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;


namespace RockEngine.Vulkan.ECS
{
    internal class RenderableObject : IDisposable
    {
        public Vertex[] Vertices { get; private set; }
        public uint[]? Indicies { get; private set; }

        private VulkanBuffer _vertexBuffer;
        private VulkanBuffer? _indexBuffer;

        public RenderableObject(Vertex[] vertices, uint[]? indicies = null)
        {
            Vertices = vertices;
            Indicies = indicies;
        }

        public async Task CreateBuffersAsync(VulkanContext context)
        {
            ulong vertexBufferSize = (ulong)(Vertices.Length * Vertex.Size);

            // Create Staging Buffer for Vertex Data
            VulkanBuffer stagingBuffer = new VulkanBufferBuilder(context.Api, context.Device)
                .Configure(SharingMode.Exclusive, vertexBufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
                .Build();

           await stagingBuffer.SendDataAsync(Vertices).ConfigureAwait(false);

            // Create Vertex Buffer with Device Local Memory
            _vertexBuffer = new VulkanBufferBuilder(context.Api, context.Device)
                .Configure(SharingMode.Exclusive, vertexBufferSize, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit)
                .Build();

            // Copy Data from Staging Buffer to Vertex Buffer
            CopyBuffer(context, stagingBuffer, _vertexBuffer, vertexBufferSize);

            stagingBuffer.Dispose();

            if (Indicies != null)
            {
                ulong indexBufferSize = (ulong)(Indicies.Length * sizeof(uint));

                // Create Staging Buffer for Index Data
                VulkanBuffer stagingIndexBuffer = new VulkanBufferBuilder(context.Api, context.Device)
                    .Configure(SharingMode.Exclusive, indexBufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
                    .Build();

                await stagingIndexBuffer.SendDataAsync(Indicies).ConfigureAwait(false);

                // Create Index Buffer with Device Local Memory
                _indexBuffer = new VulkanBufferBuilder(context.Api, context.Device)
                    .Configure(SharingMode.Exclusive, indexBufferSize, BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit)
                    .Build();

                // Copy Data from Staging Buffer to Index Buffer
                CopyBuffer(context, stagingIndexBuffer, _indexBuffer, indexBufferSize);

                stagingIndexBuffer.Dispose();
            }
        }

        private void CopyBuffer(VulkanContext context, VulkanBuffer srcBuffer, VulkanBuffer dstBuffer, ulong size)
        {
            VulkanCommandBuffer commandBuffer = new VulkanCommandBufferBuilder(context)
                .WithLevel(CommandBufferLevel.Primary)
                .Build();

            commandBuffer.Begin(new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            });

            commandBuffer.CopyBuffer(srcBuffer, dstBuffer, size);

            commandBuffer.End();

            unsafe
            {
                var buffer = commandBuffer.CommandBuffer;
                SubmitInfo submitInfo = new SubmitInfo()
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = &buffer
                };
                context.Api.QueueSubmit(context.Device.GraphicsQueue, 1, [submitInfo], default);
            }

            context.Api.QueueWaitIdle(context.Device.GraphicsQueue);
            commandBuffer.Dispose();
        }

        public void BindBuffers(VulkanContext context,VulkanCommandBuffer commandBuffer)
        {
            var buffer = _vertexBuffer.Buffer;
            ulong offset = 0;
            context.Api.CmdBindVertexBuffers(commandBuffer.CommandBuffer, 0, 1, in buffer,  in offset);

            if (_indexBuffer != null)
            {
                context.Api.CmdBindIndexBuffer(commandBuffer.CommandBuffer, _indexBuffer.Buffer, 0, IndexType.Uint32);
            }
        }

        public void Draw(VulkanContext context,VulkanCommandBuffer commandBuffer)
        {
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
        }
    }
}