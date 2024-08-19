using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    public class MeshComponentRenderer : IComponentRenderer<MeshComponent>, IDisposable
    {
        private BufferWrapper _vertexBuffer;
        private BufferWrapper _indexBuffer;
        private bool _isReady;
        private MeshComponent _component;
        private readonly VulkanContext _context;
        private readonly PipelineManager _pipelineManager;

        public MeshComponentRenderer(VulkanContext context, PipelineManager pipelineManager)
        {
            _context = context;
            _pipelineManager = pipelineManager;
        }

        public ValueTask InitializeAsync(MeshComponent component)
        {
            _component = component;
            return CreateBuffersAsync(_context, component);
        }

        public ValueTask RenderAsync(MeshComponent component, FrameInfo frameInfo)
        {
            _pipelineManager.BindQueuedDescriptorSets(frameInfo);
            return Draw(frameInfo.CommandBuffer!, _component);
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
            var t1 = stagingBuffer.CopyBufferAsync(context, _vertexBuffer, vertexBufferSize);

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
                await stagingIndexBuffer.CopyBufferAsync(context, _indexBuffer, indexBufferSize);
            }

            await t1;

            _isReady = true;
        }

        /// <summary>
        /// Drawing mesh to the commandBuffer,
        /// if mesh has indices, then used indexed drawing, else default 
        /// </summary>
        /// <param name="commandBuffer">Command buffer to which operate</param>
        /// <param name="component">MeshComponent, not used directly here, as everything that needed is stored in the renderer</param>

        public ValueTask Draw(CommandBufferWrapper commandBuffer, MeshComponent component)
        {
            if (!_isReady)
            {
                return ValueTask.CompletedTask;
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
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _isReady = false;
        }
    }
}