using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Vulkan
{
    public sealed class StagingManager : IDisposable
    {
        private  VkBuffer _stagingBuffer;
        private ulong _bufferOffset;
        private ulong _bufferSize;

        private readonly VulkanContext _context;
        private readonly Lock _bufferLock = new();
        private readonly SubmitContext _submitContext;
        private readonly ulong _alignment;

        public VkBuffer StagingBuffer => _stagingBuffer;

        public StagingManager(VulkanContext context, SubmitContext submitContext, ulong initialSize = 1 * 1024 * 1024)
        {
            _context = context;
            _submitContext = submitContext;
            _bufferSize = initialSize;
            _stagingBuffer = VkBuffer.Create(context, _bufferSize,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            _stagingBuffer.LabelObject("StagingBuffer");

            _alignment = context.Device.PhysicalDevice.Properties.Limits.MinMemoryMapAlignment;
        }

        public unsafe bool TryStage<T>(UploadBatch batch, T[] data, out ulong offset, out ulong size) where T : unmanaged
        {
            return TryStageInternal(batch, data.AsSpan(), out offset, out size);
        }

        public unsafe bool TryStage<T>(UploadBatch batch, Span<T> data, out ulong offset, out ulong size) where T : unmanaged
        {
            return TryStageInternal(batch,data, out offset, out size);
        }
        private unsafe bool TryStageInternal<T>(UploadBatch batch, Span<T> data, out ulong offset, out ulong size) where T : unmanaged
        {
            size = (ulong)(Unsafe.SizeOf<T>() * data.Length);
            offset = 0;

            lock (_bufferLock)
            {
                // Align offset
                var alignedOffset = _bufferOffset + _alignment - 1 & ~(_alignment - 1);

                // Check if we need to resize
                if (alignedOffset + size > _bufferSize)
                {
                    ResizeBuffer(batch, alignedOffset + size);
                    // After resize, aligned offset should be at start of new buffer
                    alignedOffset = 0;
                }

                // Verify we have space after potential resize
                if (alignedOffset + size > _bufferSize)
                {
                    return false;
                }

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
        private void ResizeBuffer(UploadBatch batch, ulong requiredSize)
        {
            lock (_bufferLock)
            {
                // Calculate new size (at least double current size)
                ulong newSize = Math.Max(_bufferSize * 2, requiredSize);

                // Create new buffer
                var newBuffer = VkBuffer.Create(_context, newSize,
                    BufferUsageFlags.TransferSrcBit,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);


                // Retire current buffer
                batch.AddDependency(StagingBuffer);

                // Switch to new buffer
                _stagingBuffer = newBuffer;
                _stagingBuffer.LabelObject("StagingBuffer");

                _bufferSize = newSize;
                _bufferOffset = 0;
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
