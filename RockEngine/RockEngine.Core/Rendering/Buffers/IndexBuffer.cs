using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Buffers
{
    public sealed class IndexBuffer : IDisposable
    {
        private readonly VulkanContext _context;
        private VkBuffer _buffer;
        private ulong _indexCount;
        private IndexType _indexType;
        private bool _disposed;

        public VkBuffer Buffer => _buffer;
        public ulong IndexCount => _indexCount;
        public ulong Size => _buffer?.Size ?? 0;
        public IndexType Type => _indexType;

        public IndexBuffer(VulkanContext context)
        {
            _context = context;
        }

        public unsafe void Create(ReadOnlySpan<uint> indices, BufferUsageFlags additionalUsage = BufferUsageFlags.None)
        {
            if (indices.IsEmpty)
            {
                throw new ArgumentException("Index data cannot be empty", nameof(indices));
            }

            _indexCount = (ulong)indices.Length;
            _indexType = IndexType.Uint32;
            ulong size = (ulong)(indices.Length * sizeof(uint));

            var stagingBuffer = VkBuffer.Create(
                _context,
                size,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );

            try
            {

                _buffer = VkBuffer.Create(
                    _context,
                    size,
                    BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | additionalUsage,
                    MemoryPropertyFlags.DeviceLocalBit
                );
                var transferBatch = _context.TransferSubmitContext.CreateBatch();
                transferBatch.StageToBuffer(indices,_buffer, 0, size);


                var fence = VkFence.CreateNotSignaled(_context);
                _context.TransferSubmitContext.FlushSingle(transferBatch, fence).Wait();
                fence.Dispose();
            }
            finally
            {
                stagingBuffer.Dispose();
            }
        }

        public unsafe void Create(ReadOnlySpan<ushort> indices, BufferUsageFlags additionalUsage = BufferUsageFlags.None)
        {
            if (indices.IsEmpty)
            {
                throw new ArgumentException("Index data cannot be empty", nameof(indices));
            }

            _indexCount = (ulong)indices.Length;
            _indexType = IndexType.Uint16;
            ulong size = (ulong)(indices.Length * sizeof(ushort));


            _buffer = VkBuffer.Create(
                _context,
                size,
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | additionalUsage,
                MemoryPropertyFlags.DeviceLocalBit
            );
            var transferBatch = _context.TransferSubmitContext.CreateBatch();
            transferBatch.StageToBuffer(indices, _buffer, 0, size);



            var fence = VkFence.CreateNotSignaled(_context);
            _context.TransferSubmitContext.FlushSingle(transferBatch, fence).Wait();
            fence.Dispose();

        }

        public unsafe void Update(ReadOnlySpan<uint> indices, ulong offset = 0)
        {
            if (_buffer == null)
            {
                throw new InvalidOperationException("Buffer not created");
            }

            if (_indexType != IndexType.Uint32)
            {
                throw new InvalidOperationException("Buffer was created with ushort indices");
            }

            if (offset + (ulong)(indices.Length * sizeof(uint)) > _buffer.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Update exceeds buffer size");
            }

            var transferBatch = _context.TransferSubmitContext.CreateBatch();
            transferBatch.StageToBuffer(indices, _buffer, offset, (ulong)(indices.Length * sizeof(uint)));
            var fence = VkFence.CreateNotSignaled(_context);
            _context.TransferSubmitContext.FlushSingle(transferBatch, fence).Wait();
            fence.Dispose();

        }

        public void Bind(VkCommandBuffer commandBuffer, ulong offset = 0)
        {
            if (_buffer == null)
            {
                throw new InvalidOperationException("Buffer not created");
            }

            _buffer.BindIndexBuffer(commandBuffer, offset, _indexType);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _buffer?.Dispose();
                _disposed = true;
            }
        }
    }
}