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

        public MeshComponentRenderer(MeshComponent component)
        {
            _component = component;
        }

        public ValueTask InitializeAsync(MeshComponent component, VulkanContext context)
        {
            return CreateBuffersAsync(context, _component);
        }

        public Task RenderAsync(MeshComponent component, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            Draw(context, commandBuffer, _component);
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
            stagingBuffer.CopyBuffer(context, _vertexBuffer, vertexBufferSize);

            if (component.Indicies != null)
            {
                ulong indexBufferSize = (ulong)(component.Indicies.Length * sizeof(uint));

                // Create Staging Buffer for Index Data
                BufferCreateInfo stagingIndexBufferCreateInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = indexBufferSize,
                    Usage = BufferUsageFlags.TransferSrcBit,
                    SharingMode = SharingMode.Exclusive
                };

                using BufferWrapper stagingIndexBuffer = BufferWrapper.Create(context, in stagingIndexBufferCreateInfo, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

                await stagingIndexBuffer.SendDataAsync(component.Indicies)
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
                stagingIndexBuffer.CopyBuffer(context, _indexBuffer, indexBufferSize);
            }

            _isReady = true;
        }


        public void Draw(VulkanContext context, CommandBufferWrapper commandBuffer, MeshComponent component)
        {
            if (!_isReady)
            {
                return;
            }
            _vertexBuffer.BindVertexBuffer(commandBuffer);
            if (_indexBuffer != null)
            {
                _indexBuffer.BindIndexBuffer(commandBuffer);
                commandBuffer.DrawIndexed((uint)component.Indicies!.Length, 1, 0, 0, 0);
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