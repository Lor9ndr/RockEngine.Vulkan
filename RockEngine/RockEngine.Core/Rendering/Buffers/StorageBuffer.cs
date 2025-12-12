using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering.Buffers
{
    public sealed class StorageBuffer<T> : IDisposable where T : unmanaged
    {
        private readonly VulkanContext _context;
        private VkBuffer _deviceBuffer; // Changed from readonly to allow resizing
        private readonly ulong _stride;
        private bool _disposed;

        public VkBuffer Buffer => _deviceBuffer;
        public ulong Capacity { get; private set; } // Added private setter
        public ulong Stride => _stride;

        public StorageBuffer(VulkanContext context, ulong capacity)
        {
            _context = context;
            Capacity = capacity;

            var elementSize = (ulong)Unsafe.SizeOf<T>();
            var alignment = context.Device.PhysicalDevice.Properties.Limits.MinStorageBufferOffsetAlignment;
            _stride = elementSize + alignment - 1 & ~(alignment - 1);

            _deviceBuffer = VkBuffer.Create(
                context,
                Capacity * _stride,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit, // Added TransferSrcBit for resize operations
                MemoryPropertyFlags.DeviceLocalBit);
        }

        /// <summary>
        /// Resizes the buffer to a new capacity
        /// </summary>
        public void Resize(ulong newCapacity, UploadBatch batch)
        {
            if (newCapacity == Capacity)
            {
                return;
            }

            // Create new buffer with the desired capacity
            var newBuffer = VkBuffer.Create(
                _context,
                newCapacity * _stride,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            // Copy existing data to new buffer if there's data to preserve
            if (Capacity > 0)
            {
                var copySize = Math.Min(Capacity, newCapacity) * _stride;
                var copyRegion = new BufferCopy
                {
                    SrcOffset = 0,
                    DstOffset = 0,
                    Size = copySize
                };

                // Add barrier for source buffer
                var srcBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit,
                    DstAccessMask = AccessFlags.TransferReadBit,
                    Buffer = _deviceBuffer,
                    Offset = 0,
                    Size = Vk.WholeSize
                };

                batch.PipelineBarrier(
                    srcStage: PipelineStageFlags.VertexInputBit,
                    dstStage: PipelineStageFlags.TransferBit,
                    bufferMemoryBarriers: new[] { srcBarrier }
                );

                // Add barrier for destination buffer
                var dstBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags.TransferWriteBit,
                    Buffer = newBuffer,
                    Offset = 0,
                    Size = Vk.WholeSize
                };

                batch.PipelineBarrier(
                    srcStage: PipelineStageFlags.TopOfPipeBit,
                    dstStage: PipelineStageFlags.TransferBit,
                    bufferMemoryBarriers: new[] { dstBarrier }
                );

                // Copy data
                batch.CopyBuffer(_deviceBuffer, newBuffer, in  copyRegion);

                // Add barrier for destination buffer after copy
                var postDstBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit,
                    Buffer = newBuffer,
                    Offset = 0,
                    Size = Vk.WholeSize
                };

                batch.PipelineBarrier(
                    srcStage: PipelineStageFlags.TransferBit,
                    dstStage: PipelineStageFlags.VertexInputBit,
                    bufferMemoryBarriers: new[] { postDstBarrier }
                );
            }

            // Dispose old buffer and replace with new one
            batch.AddDependency(_deviceBuffer);
            _deviceBuffer = newBuffer;
            Capacity = newCapacity;
        }

        /// <summary>
        /// Resizes the buffer asynchronously
        /// </summary>
        public async ValueTask ResizeAsync(ulong newCapacity)
        {
            if (newCapacity == Capacity)
            {
                return;
            }

            var batch = _context.TransferSubmitContext.CreateBatch();
            Resize(newCapacity, batch);
            await _context.TransferSubmitContext.FlushSingle(batch, VkFence.CreateNotSignaled(_context));
        }

        public void StageData(UploadBatch batch, T[] data, ulong startIndex = 0)
        {
            if ((ulong)data.Length + startIndex > Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(data), "Exceeds buffer capacity");
            }

            batch.StageToBuffer<T>(
                data.AsSpan(),
                _deviceBuffer,
                startIndex * _stride,
                (ulong)(Unsafe.SizeOf<T>() * data.Length)
            );
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _deviceBuffer?.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        ~StorageBuffer()
        {
            Dispose();
        }
    }
}