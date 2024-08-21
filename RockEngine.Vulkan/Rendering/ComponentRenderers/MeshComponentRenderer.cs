using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VkObjects.Infos.Texture;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Vulkan.Rendering.ComponentRenderers
{
    public sealed class MeshComponentRenderer : IComponentRenderer<MeshComponent>, IDisposable
    {
        private BufferWrapper _vertexBuffer;
        private BufferWrapper _indexBuffer;
        private bool _isReady;
        private UniformBufferObject _materialUbo;
        private readonly VulkanContext _context;
        private readonly PipelineManager _pipelineManager;

        public MeshComponentRenderer(VulkanContext context, PipelineManager pipelineManager)
        {
            _context = context;
            _pipelineManager = pipelineManager;
        }

        public async ValueTask InitializeAsync(MeshComponent component)
        {
            await CreateBuffersAsync(component);

            if (component.Material is null)
            {
                component.SetMaterial(new MaterialRendering.Material(_pipelineManager.GetEffect("ColorLit"), null, new Dictionary<string, object>()
                {
                    { "baseColor", new Vector4(0.7f,0.2f, 0.2f,1.0f) },
                    { "normalColor", new Vector3(1)}
                }));
            }

            if (component.Material!.Textures != null )
            {
                var loadTasks = component.Material.Textures
                  .Where(t => t.TextureInfo is NotLoadedTextureInfo)
                  .Select(t => t.LoadAsync(_context));
                await Task.WhenAll(loadTasks);
            }
            _materialUbo = UniformBufferObject.Create(_context, 28, "MaterialParams");



            _pipelineManager.SetMaterialDescriptors(component.Material, _materialUbo);

            _isReady = true;
        }

        public async ValueTask RenderAsync(MeshComponent component, FrameInfo frameInfo)
        {
            if (component.Material.Parameters.Count != 0)
            {
                _materialUbo.UniformBuffer.MapMemory();
                await _materialUbo.UniformBuffer.SendDataMappedAsync(component.Material.Parameters);
                _materialUbo.UniformBuffer.UnmapMemory();
            }

            _pipelineManager.Use(component.Material, frameInfo);
            _pipelineManager.BindQueuedDescriptorSets(frameInfo);

            Draw(frameInfo.CommandBuffer!, component);
        }

        private async ValueTask CreateBuffersAsync(MeshComponent component)
        {
            await CreateVertexBufferAsync(component);
            await CreateIndexBufferAsync(component);
        }

        private async ValueTask CreateVertexBufferAsync(MeshComponent component)
        {
            ulong vertexBufferSize = (ulong)(component.Vertices.Length * Vertex.Size);
            _vertexBuffer = await CreateDeviceLocalBufferAsync(vertexBufferSize, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit, component.Vertices);
        }

        private async ValueTask CreateIndexBufferAsync(MeshComponent component)
        {
            if (component.Indices != null)
            {
                ulong indexBufferSize = (ulong)(component.Indices.Length * sizeof(uint));
                _indexBuffer = await CreateDeviceLocalBufferAsync(indexBufferSize, BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit, component.Indices);
            }
        }

        private async ValueTask<BufferWrapper> CreateDeviceLocalBufferAsync<T>(ulong bufferSize, BufferUsageFlags usage, T[] data) where T : unmanaged
        {
            using var stagingBuffer = BufferWrapper.Create(_context, new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = bufferSize,
                Usage = BufferUsageFlags.TransferSrcBit,
                SharingMode = SharingMode.Exclusive
            }, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            await stagingBuffer.SendDataAsync(new ReadOnlyMemory<T>(data));

            var deviceLocalBuffer = BufferWrapper.Create(_context, new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = bufferSize,
                Usage = usage,
                SharingMode = SharingMode.Exclusive
            }, MemoryPropertyFlags.DeviceLocalBit);

            stagingBuffer.CopyBuffer(_context, deviceLocalBuffer, bufferSize);

            return deviceLocalBuffer;
        }

        private unsafe void Draw(CommandBufferWrapper commandBuffer, MeshComponent component)
        {
            if (!_isReady)
            {
                return;
            }

            _vertexBuffer.BindVertexBuffer(commandBuffer);

            if (_indexBuffer != null)
            {
                _indexBuffer.BindIndexBuffer(commandBuffer);
                _context.Api.CmdDrawIndexed(commandBuffer, (uint)component.Indices.Length, 1,0,0,0);
            }
            else
            {
                _context.Api.CmdDraw(commandBuffer, (uint)component.Vertices.Length,1,0,0);
            }

        }
        public ValueTask UpdateAsync(MeshComponent component)
        {
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
