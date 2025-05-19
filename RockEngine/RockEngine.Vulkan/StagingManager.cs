using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering.Managers
{
    public sealed class StagingManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly VkBuffer _stagingBuffer;
        private readonly Lock _bufferLock = new();
        private ulong _bufferOffset;
        private readonly ulong _bufferSize;
        private readonly ulong _alignment;

        public VkBuffer StagingBuffer => _stagingBuffer;

        public StagingManager(VulkanContext context, ulong initialSize = 512 * 1024 * 1024)
        {
            _context = context;
            _bufferSize = initialSize;
            _stagingBuffer = VkBuffer.Create(context, _bufferSize,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _alignment = context.Device.PhysicalDevice.Properties.Limits.MinMemoryMapAlignment;
        }

        public unsafe bool TryStage<T>(T[] data, out ulong offset, out ulong size) where T : unmanaged
        {
            size = (ulong)(Unsafe.SizeOf<T>() * data.Length);
            offset = 0;

            lock (_bufferLock)
            {
                // Align offset
                var alignedOffset = (_bufferOffset + _alignment - 1) & ~(_alignment - 1);

                if (alignedOffset + size > _bufferSize)
                    return false;

                void* mappedPtr = null;
                _stagingBuffer.Map(ref mappedPtr, size, alignedOffset);

                fixed (T* dataPtr = data)
                {
                    System.Buffer.MemoryCopy(
                        dataPtr,
                        (byte*)mappedPtr + alignedOffset,
                        _bufferSize - alignedOffset,
                        size);
                }

                offset = alignedOffset;
                _bufferOffset = alignedOffset + size;
                return true;
            }
        }
        public unsafe bool TryStage<T>(Span<T> data, out ulong offset, out ulong size) where T : unmanaged
        {
            size = (ulong)(Unsafe.SizeOf<T>() * data.Length);
            offset = 0;

            lock (_bufferLock)
            {
                // Align offset
                var alignedOffset = (_bufferOffset + _alignment - 1) & ~(_alignment - 1);

                if (alignedOffset + size > _bufferSize)
                    return false;

                void* mappedPtr = null;
                _stagingBuffer.Map(ref mappedPtr, size, alignedOffset);

                fixed (T* dataPtr = data)
                {
                    System.Buffer.MemoryCopy(
                        dataPtr,
                        (byte*)mappedPtr + alignedOffset,
                        _bufferSize - alignedOffset,
                        size);
                }

                offset = alignedOffset;
                _bufferOffset = alignedOffset + size;
                return true;
            }
        }

        public void Reset()
        {
            lock (_bufferLock)
            {
                _bufferOffset = 0;
            }
        }

        public void Dispose() => _stagingBuffer.Dispose();
    }
}
