
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.Assets
{

    public sealed class MeshAsset : Asset<MeshData>, IGpuResource, IMeshProvider, IDisposable
    {
        public override string Type => "Mesh";

        private Vertex[]? Vertices => Data?.Vertices;
        private uint[]? Indices => Data?.Indices;

        public bool GpuReady => VertexBuffer is not null;

        public bool HasIndices => IndicesCount > 0;

        public uint? IndicesCount {get; private set;} 

        public uint VerticesCount {get; private set; }

        public VkBuffer? VertexBuffer { get; private set; }
        public VkBuffer? IndexBuffer { get; private set; }

        private readonly SemaphoreSlim _gpuLock = new SemaphoreSlim(1,1);
        private bool _disposed;


        public void SetGeometry(Vertex[] vertices, uint[] indices)
        {
            ArgumentNullException.ThrowIfNull(vertices, nameof(vertices));
            Data ??= new MeshData();

            Data.Vertices = vertices;
            Data.Indices = indices;
            VerticesCount = (uint)Vertices.Length;
            IndicesCount = (uint?)Indices.Length;
        }
        public override void SetData(object data)
        {
            if(data is MeshData meshData)
            {
                Data = meshData;
                SetGeometry(Data.Vertices, Data.Indices);
            }

        }


        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady) return;

            if (!IsDataLoaded) await  LoadDataAsync().ConfigureAwait(true);
            var renderer = IoC.Container.GetInstance<Renderer>();
            var context = VulkanContext.GetCurrent();
            await _gpuLock.WaitAsync().ConfigureAwait(true);
            try
            {
                if (GpuReady) return;

                var batch = renderer.SubmitContext.CreateBatch();

                if (Vertices!.Length == 0)
                    throw new InvalidOperationException("Mesh has no vertices");

                if (Indices?.Length > 0 && Indices.Any(i => i >= Vertices.Length))
                    throw new InvalidOperationException("Index out of vertex bounds");

                // Create vertex buffer
                VertexBuffer = VkBuffer.Create(
                    context,
                    (ulong)(Unsafe.SizeOf<Vertex>() * Vertices!.Length),
                    BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit
                );

                batch.StageToBuffer(
                    Vertices.AsSpan(),
                    VertexBuffer,
                    0,
                    (ulong)(Unsafe.SizeOf<Vertex>() * Vertices.Length)
                );

                var vertexBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.VertexAttributeReadBit,
                    Buffer = VertexBuffer,
                    Offset = 0,
                    Size = VertexBuffer.Size
                };

                // Create index buffer if needed
                if (Indices is { Length: > 0 })
                {
                    IndexBuffer = VkBuffer.Create(
                        context,
                        (ulong)(Unsafe.SizeOf<uint>() * Indices.Length),
                        BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                        MemoryPropertyFlags.DeviceLocalBit
                    );

                    batch.StageToBuffer(
                        Indices.AsSpan(),
                        IndexBuffer,
                        0,
                        (ulong)(Unsafe.SizeOf<uint>() * Indices.Length)
                    );
                    var indexBarrier = new BufferMemoryBarrier
                    {
                        SType = StructureType.BufferMemoryBarrier,
                        SrcAccessMask = AccessFlags.TransferWriteBit,
                        DstAccessMask = AccessFlags.IndexReadBit,
                        Buffer = IndexBuffer,
                        Offset = 0,
                        Size = IndexBuffer.Size
                    };

                    batch.PipelineBarrier(
                        srcStage: PipelineStageFlags.TransferBit,
                        dstStage: PipelineStageFlags.VertexInputBit,
                        bufferMemoryBarriers: [vertexBarrier, indexBarrier]
                    );
                }
                else
                {
                    batch.PipelineBarrier(
                        srcStage: PipelineStageFlags.TransferBit,
                        dstStage: PipelineStageFlags.VertexInputBit,
                        bufferMemoryBarriers: [vertexBarrier]
                    );
                }
                IndicesCount = (uint?)Indices?.Length;
                VerticesCount = (uint)Vertices.Length;
                batch.Submit();
                //await context.SubmitContext.FlushSingle(batch, VkFence.CreateNotSignaled(context));
            }
            finally
            {
                _gpuLock.Release();
            }
            UnloadData();

        }

        public override void UnloadData()
        {
            _loadSemaphore.Wait();
            try
            {
                base.UnloadData();
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }

        public void UnloadGpuResources()
        {
            lock (_gpuLock)
            {
                VertexBuffer?.Dispose();
                VertexBuffer = null;
                IndexBuffer?.Dispose();
                IndexBuffer = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            UnloadData();
            UnloadGpuResources();
            _loadSemaphore.Dispose();
            _disposed = true;
        }
    }
}
