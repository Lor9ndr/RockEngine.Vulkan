using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Vulkan
{
    public sealed class StagingManager : IDisposable
    {
        private VkBuffer _stagingBuffer;
        private ulong _bufferOffset;
        private ulong _bufferSize;

        private readonly VulkanContext _context;
        private readonly Lock _bufferLock = new();
        private readonly SubmitContext _submitContext;
        private readonly ulong _alignment;
        private readonly ulong _initialSize;
        private readonly TimeSpan _idleTimeThreshold = TimeSpan.FromSeconds(5);

        private ulong _maxUsedOffset;
        private bool _shouldDownsize;
        private ulong _downsizeTarget;
        private DateTime _lastResetTime;

        public VkBuffer StagingBuffer => _stagingBuffer;

        public StagingManager(VulkanContext context,  ulong initialSize = 1 * 1024) 
        {
            _context = context;
            _bufferSize = initialSize;
            _initialSize = initialSize;
            _stagingBuffer = VkBuffer.Create(context, _bufferSize,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            _stagingBuffer.LabelObject("StagingBuffer");

            _alignment = context.Device.PhysicalDevice.Properties.Limits.MinMemoryMapAlignment;
            _lastResetTime = DateTime.UtcNow;
        }

        public unsafe bool TryStage<T>(UploadBatch batch, T[] data, out ulong offset, out ulong size) where T : unmanaged
        {
            return TryStageInternal(batch, data.AsSpan(), out offset, out size);
        }

        public unsafe bool TryStage<T>(UploadBatch batch, ReadOnlySpan<T> data, out ulong offset, out ulong size) where T : unmanaged
        {
            return TryStageInternal(batch, data, out offset, out size);
        }

        private bool TryStageInternal<T>(UploadBatch batch, ReadOnlySpan<T> data, out ulong offset, out ulong size) where T : unmanaged
        {
            size = (ulong)(Unsafe.SizeOf<T>() * data.Length);
            lock (_bufferLock)
            {
                // Check if we need to downsize before processing this allocation
                if (_shouldDownsize)
                {
                    // Calculate required space including alignment
                    ulong alignedOffsetForCheck = _bufferOffset + _alignment - 1 & ~(_alignment - 1);
                    ulong requiredForCurrent = alignedOffsetForCheck + size;

                    if (requiredForCurrent <= _downsizeTarget)
                    {
                        // Downsize to target and reset state
                        ResizeBuffer(batch, _downsizeTarget);
                        _shouldDownsize = false;
                    }
                    else
                    {
                        // Can't downsize - allocation too large for target
                        _shouldDownsize = false;
                    }
                }

                // Calculate aligned offset for current allocation
                ulong alignedOffset = _bufferOffset + _alignment - 1 & ~(_alignment - 1);

                // Check if we need to grow buffer
                if (alignedOffset + size > _bufferSize)
                {
                    // Grow buffer to at least double current size or required size
                    ulong newSize = Math.Max(_bufferSize * 2, alignedOffset + size);
                    ResizeBuffer(batch, newSize);
                    alignedOffset = 0;
                }

                // Map buffer and copy data
                using (var mem = _stagingBuffer.MapMemory(size, alignedOffset))
                {
                    data.CopyTo(mem.GetSpan<T>());
                }


                // Create memory barrier
             /*   var bufferBarrier = new BufferMemoryBarrier2
                {
                    SType = StructureType.BufferMemoryBarrier2,
                    SrcAccessMask = AccessFlags2.HostWriteBit,
                    DstAccessMask = AccessFlags2.TransferReadBit,
                    Buffer = _stagingBuffer,
                    Offset = alignedOffset,
                    Size = size,
                    SrcStageMask = PipelineStageFlags2.HostBit,
                    DstStageMask = PipelineStageFlags2.TransferBit
                };

                // Add to command batch
                batch.PipelineBarrier([],[ bufferBarrier], []);*/

                // Update state
                offset = alignedOffset;
                _bufferOffset = alignedOffset + size;
                _maxUsedOffset = Math.Max(_maxUsedOffset, _bufferOffset);

                return true;
            }
        }

        private void ResizeBuffer(UploadBatch batch, ulong newSize)
        {
            // Create new buffer
            var newBuffer = VkBuffer.Create(_context, newSize,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            // Retire old buffer
            batch.AddDependency(_stagingBuffer);

            // Update references
            _stagingBuffer = newBuffer;
            _stagingBuffer.LabelObject("StagingBuffer");
            _bufferSize = newSize;
            _bufferOffset = 0;
        }

        public void Reset()
        {
            lock (_bufferLock)
            {
                ulong currentMax = _maxUsedOffset;
                _maxUsedOffset = 0;
                _bufferOffset = 0;

                // Check if we should schedule downsize
                DateTime now = DateTime.UtcNow;
                TimeSpan timeSinceLastReset = now - _lastResetTime;
                _lastResetTime = now;

                if (timeSinceLastReset > _idleTimeThreshold &&
                    currentMax < _bufferSize / 2 &&
                    _bufferSize > _initialSize)
                {
                    // Calculate target size (at least initial size)
                    _downsizeTarget = Math.Max(_initialSize, currentMax * 2);
                    _shouldDownsize = true;
                }
            }
        }

        public void Dispose() => _stagingBuffer.Dispose();
    }
}