using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    internal class MeshComponentRenderer : IComponentRenderer<MeshComponent>, IDisposable
    {
        private BufferWrapper _vertexBuffer;
        private BufferWrapper _indexBuffer;
        private bool _isReady;
        private readonly MeshComponent _component;
        private readonly VulkanContext _context;

        public MeshComponentRenderer(MeshComponent component, VulkanContext context)
        {
            _component = component;
            _context = context;
        }

        public ValueTask InitializeAsync(MeshComponent component)
        {
            return CreateBuffersAsync(_context, _component);
        }

        public Task RenderAsync(MeshComponent component, CommandBufferWrapper commandBuffer)
        {
            Draw(commandBuffer, _component);
            return Task.CompletedTask;
        }

        private async ValueTask CreateBuffersAsync(VulkanContext context, MeshComponent component)
        {
            ulong vertexBufferSize = (ulong)(component.Vertices.Length * Vertex.Size);

            // Create Staging Buffer for Vertex Data
            BufferCreateInfo stagingBufferCreateInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = vertexBufferSize,
                Usage = BufferUsageFlags.TransferSrcBit,
                SharingMode = SharingMode.Exclusive
            };

            using BufferWrapper stagingBuffer = BufferWrapper.Create(context, in stagingBufferCreateInfo, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            await stagingBuffer.SendDataAsync(component.Vertices)
                .ConfigureAwait(false);

            // Create Vertex Buffer with Device Local Memory
            BufferCreateInfo vertexBufferCreateInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = vertexBufferSize,
                Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive
            };

            _vertexBuffer = BufferWrapper.Create(context, in vertexBufferCreateInfo, MemoryPropertyFlags.DeviceLocalBit);

            // Copy Data from Staging Buffer to Vertex Buffer
            var t1 =  stagingBuffer.CopyBufferAsync(context, _vertexBuffer, vertexBufferSize);

            if (component.Indices != null)
            {
                ulong indexBufferSize = (ulong)(component.Indices.Length * sizeof(uint));

                // Create Staging Buffer for Index Data
                BufferCreateInfo stagingIndexBufferCreateInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = indexBufferSize,
                    Usage = BufferUsageFlags.TransferSrcBit,
                    SharingMode = SharingMode.Exclusive
                };

                using BufferWrapper stagingIndexBuffer = BufferWrapper.Create(context, in stagingIndexBufferCreateInfo, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

                await stagingIndexBuffer.SendDataAsync(component.Indices)
                    .ConfigureAwait(false);

                // Create Index Buffer with Device Local Memory
                BufferCreateInfo indexBufferCreateInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = indexBufferSize,
                    Usage = BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                    SharingMode = SharingMode.Exclusive
                };

                _indexBuffer = BufferWrapper.Create(context, in indexBufferCreateInfo, MemoryPropertyFlags.DeviceLocalBit);

                // Copy Data from Staging Buffer to Index Buffer
                await stagingBuffer.CopyBufferAsync(context, _indexBuffer, indexBufferSize);
            }

            await t1;

            _isReady = true;
        }


        public void Draw(CommandBufferWrapper commandBuffer, MeshComponent component)
        {
            if (!_isReady)
            {
                return;
            }
            _vertexBuffer.BindVertexBuffer(commandBuffer);
            if (_indexBuffer != null)
            {
                _indexBuffer.BindIndexBuffer(commandBuffer);
                commandBuffer.DrawIndexed((uint)component.Indices!.Length, 1, 0, 0, 0);
            }
            else
            {
               commandBuffer.Draw((uint)component.Vertices.Length, 1, 0, 0);
            }
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _isReady = false;
        }
    }
}