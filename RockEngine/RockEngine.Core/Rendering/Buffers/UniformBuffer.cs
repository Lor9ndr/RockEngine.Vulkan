using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Buffers
{
    /// <summary>
    /// Represents a Vulkan uniform buffer that is host-visible and host-coherent.
    /// Provides direct mapping for fast updates.
    /// </summary>
    public class UniformBuffer : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly VkBuffer _buffer;
        private readonly ulong _requestedSize;
        private readonly bool _isDynamic;
        private bool _disposed;

        /// <summary>
        /// Gets the underlying Vulkan buffer.
        /// </summary>
        public VkBuffer Buffer => _buffer;

        /// <summary>
        /// Indicates whether this buffer is intended to be used with dynamic uniform buffer offsets.
        /// </summary>
        public bool IsDynamic => _isDynamic;

        /// <summary>
        /// Gets the requested size of the buffer (the size passed to the constructor).
        /// </summary>
        public ulong Size => _requestedSize;

        /// <summary>
        /// Gets the actual aligned size of the Vulkan buffer (may be larger than <see cref="Size"/>).
        /// </summary>
        public ulong AlignedSize => _buffer.Size;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniformBuffer"/> class.
        /// </summary>
        /// <param name="context">The Vulkan context.</param>
        /// <param name="size">The requested buffer size in bytes.</param>
        /// <param name="isDynamic">If set to <c>true</c>, indicates this buffer will be used with dynamic offsets.</param>
        public UniformBuffer(VulkanContext context, ulong size, bool isDynamic = false)
        {
            _context = context;
            _requestedSize = size;
            _isDynamic = isDynamic;

            var bufferUsage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit;
            var memoryProperties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

            // VkBuffer.Create aligns the size according to Vulkan requirements.
            // The actual buffer may be larger, but writes are restricted to the requested size.
            _buffer = VkBuffer.Create(context, size, bufferUsage, memoryProperties);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UniformBuffer"/> class using the current Vulkan context.
        /// </summary>
        /// <param name="size">The requested buffer size in bytes.</param>
        /// <param name="isDynamic">If set to <c>true</c>, indicates this buffer will be used with dynamic offsets.</param>
        public UniformBuffer(ulong size, bool isDynamic = false)
            : this(VulkanContext.GetCurrent(), size, isDynamic)
        {
        }

        /// <summary>
        /// Updates the buffer with a single unmanaged value.
        /// </summary>
        /// <typeparam name="T">Unmanaged type of the data.</typeparam>
        /// <param name="data">The data to write.</param>
        /// <param name="size">Number of bytes to write (use <see cref="Vk.WholeSize"/> for the whole data).</param>
        /// <param name="offset">Offset in bytes from the start of the buffer.</param>
        /// <exception cref="ArgumentException">Thrown if the data range exceeds the requested buffer size.</exception>
        public void Update<T>(in T data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            var span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in data), 1);
            UpdateInternal(span, size, offset);
        }

        /// <summary>
        /// Updates the buffer with an array of unmanaged values.
        /// </summary>
        /// <typeparam name="T">Unmanaged type of the data.</typeparam>
        /// <param name="data">The data array.</param>
        /// <param name="size">Number of bytes to write (use <see cref="Vk.WholeSize"/> for the whole array).</param>
        /// <param name="offset">Offset in bytes from the start of the buffer.</param>
        /// <exception cref="ArgumentException">Thrown if the data range exceeds the requested buffer size.</exception>
        public void Update<T>(T[] data, ulong size = Vk.WholeSize, ulong offset = 0) where T : unmanaged
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            UpdateInternal(data.AsSpan(), size, offset);
        }

        /// <summary>
        /// Updates the buffer with a span of bytes.
        /// </summary>
        /// <param name="data">The byte data.</param>
        /// <param name="size">Number of bytes to write (use <see cref="Vk.WholeSize"/> for the whole span).</param>
        /// <param name="offset">Offset in bytes from the start of the buffer.</param>
        /// <exception cref="ArgumentException">Thrown if the data range exceeds the requested buffer size.</exception>
        public void Update(Span<byte> data, ulong size = Vk.WholeSize, ulong offset = 0)
        {
            if (data.IsEmpty) throw new ArgumentException("Data cannot be empty", nameof(data));
            UpdateInternal(data, size, offset);
        }

        private void UpdateInternal<T>(ReadOnlySpan<T> data, ulong size, ulong offset) where T : unmanaged
        {
            ulong dataSize = (ulong)(data.Length * Unsafe.SizeOf<T>());
            ulong actualSize = size == Vk.WholeSize ? dataSize : size;

            if (actualSize > dataSize)
                throw new ArgumentException("Specified size exceeds data size", nameof(size));
            if (offset + actualSize > _requestedSize)
                throw new ArgumentException("Data range exceeds buffer requested size");

            using var mapped = _buffer.MapMemory(actualSize, offset);
            var destSpan = mapped.GetSpan<T>();
            data.CopyTo(destSpan);
            mapped.Flush(); // Flush only if necessary (MappedMemory.Flush checks host-coherent)
        }

        private void UpdateInternal(ReadOnlySpan<byte> data, ulong size, ulong offset)
        {
            ulong dataSize = (ulong)data.Length;
            ulong actualSize = size == Vk.WholeSize ? dataSize : size;

            if (actualSize > dataSize)
                throw new ArgumentException("Specified size exceeds data size", nameof(size));
            if (offset + actualSize > _requestedSize)
                throw new ArgumentException("Data range exceeds buffer requested size");

            using var mapped = _buffer.MapMemory(actualSize, offset);
            var destSpan = mapped.GetSpan();
            data[..(int)actualSize].CopyTo(destSpan);
            mapped.Flush();
        }

        /// <summary>
        /// Flushes the mapped memory range if the buffer is not host-coherent.
        /// </summary>
        /// <param name="size">Number of bytes to flush (use <see cref="Vk.WholeSize"/> for the whole buffer).</param>
        /// <param name="offset">Offset in bytes from the start of the buffer.</param>
        public void FlushBuffer(ulong size = Vk.WholeSize, ulong offset = 0)
        {
            _buffer.Flush(size, offset);
        }

        /// <summary>
        /// Disposes the buffer and releases all associated Vulkan resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _buffer.Dispose();
                _disposed = true;
            }
        }
    }
}