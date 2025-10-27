using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Buffers
{
    public sealed class VertexBuffer<T> : IDisposable where T : unmanaged, IVertex
    {
        private readonly VulkanContext _context;
        private VkBuffer _buffer;
        private ulong _vertexCount;
        private bool _disposed;

        public VkBuffer Buffer => _buffer;
        public ulong VertexCount => _vertexCount;
        public ulong Size => _buffer?.Size ?? 0;
        public uint Stride => (uint)Marshal.SizeOf<T>();

        public VertexBuffer(VulkanContext context)
        {
            _context = context;
        }

        public unsafe void Create(ReadOnlySpan<T> vertices, BufferUsageFlags additionalUsage = BufferUsageFlags.None)
        {
            if (vertices.IsEmpty)
            {
                throw new ArgumentException("Vertex data cannot be empty", nameof(vertices));
            }

            _vertexCount = (ulong)vertices.Length;
            ulong size = (ulong)(vertices.Length * Marshal.SizeOf<T>());

            // Create staging buffer
            var stagingBuffer = VkBuffer.Create(
                _context,
                size,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );

            try
            {

                // Create device-local buffer
                _buffer = VkBuffer.Create(
                    _context,
                    size,
                    BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | additionalUsage,
                    MemoryPropertyFlags.DeviceLocalBit
                );

                // Copy from staging to device buffer
                var transferBatch = _context.TransferSubmitContext.CreateBatch();
                transferBatch.StageToBuffer(vertices, _buffer, 0, size);

                // Submit and wait for transfer to complete
                var fence = VkFence.CreateNotSignaled(_context);
                _context.TransferSubmitContext.FlushSingle(transferBatch, fence).Wait();
                fence.Dispose();
            }
            finally
            {
                stagingBuffer.Dispose();
            }
        }

        public unsafe void Update(ReadOnlySpan<T> vertices, ulong offset = 0)
        {
            if (_buffer == null)
            {
                throw new InvalidOperationException("Buffer not created");
            }

            if (offset + (ulong)(vertices.Length * Marshal.SizeOf<T>()) > _buffer.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Update exceeds buffer size");
            }

            // For device-local buffers, we need to use staging buffer for updates
            var stagingBuffer = VkBuffer.Create(
                _context,
                (ulong)(vertices.Length * Marshal.SizeOf<T>()),
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );

            try
            {
                var transferBatch = _context.TransferSubmitContext.CreateBatch();
                transferBatch.StageToBuffer<T>(vertices, _buffer,0, _buffer.Size);
               

                var fence = VkFence.CreateNotSignaled(_context);
                _context.TransferSubmitContext.FlushSingle(transferBatch, fence).Wait();
                fence.Dispose();
            }
            finally
            {
                stagingBuffer.Dispose();
            }
        }

        public void Bind(VkCommandBuffer commandBuffer, ulong offset = 0)
        {
            if (_buffer == null)
            {
                throw new InvalidOperationException("Buffer not created");
            }

            _buffer.BindVertexBuffer(commandBuffer, offset);
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