using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public readonly ref struct MappedMemory
    {
        private readonly VkBuffer _buffer;
        private readonly nint _mappedPointer;
        private readonly ulong _size;
        private readonly ulong _offset;

        public nint Pointer => _mappedPointer;
        public ulong Size => _size;
        public ulong Offset => _offset;

        internal MappedMemory(VkBuffer buffer, nint mappedPointer, ulong size, ulong offset)
        {
            _buffer = buffer;
            _mappedPointer = mappedPointer;
            _size = size;
            _offset = offset;
        }

        /// <summary>
        /// Gets a span over the mapped memory for type T
        /// </summary>
        /// <typeparam name="T">Unmanaged type</typeparam>
        /// <returns>Span of the mapped memory</returns>
        public unsafe Span<T> GetSpan<T>() where T : unmanaged
        {

            int elementSize = sizeof(T);
            int count = (int)(_size / (ulong)elementSize);
            return new Span<T>((void*)_mappedPointer, count);
        }

        /// <summary>
        /// Gets a span of bytes over the mapped memory
        /// </summary>
        /// <returns>Span of bytes</returns>
        public unsafe Span<byte> GetSpan()
        {
            if (_size == Vk.WholeSize)
            {
                throw new InvalidOperationException("Cannot create span for WholeSize mapping");
            }

            return new Span<byte>((void*)_mappedPointer, (int)_size);
        }

        /// <summary>
        /// Flushes the mapped memory range if needed
        /// </summary>
        public void Flush()
        {
            _buffer.Flush(_size, _offset);
        }

        /// <summary>
        /// Unmaps the memory
        /// </summary>
        public void Dispose()
        {
            _buffer.Unmap();
        }
    }
}
