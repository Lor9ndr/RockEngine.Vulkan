using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering.Buffers
{
    public sealed class StorageBuffer<T> : IDisposable where T : unmanaged
    {
        private readonly VulkanContext _context;
        private VkBuffer _deviceBuffer;
        private readonly ulong _stride;
        private bool _disposed;

        public VkBuffer Buffer => _deviceBuffer;
        public ulong Capacity { get; private set; }
        public ulong Stride => _stride;

        public StorageBuffer(VulkanContext context, ulong capacity,
            BufferUsageFlags bufferUsageFlags = BufferUsageFlags.StorageBufferBit |
            BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit)
        {
            _context = context;
            Capacity = capacity;

            var elementSize = (ulong)Unsafe.SizeOf<T>();
            var alignment = context.Device.PhysicalDevice.Properties.Limits.MinStorageBufferOffsetAlignment;
            _stride = (elementSize + alignment - 1) & ~(alignment - 1);

            // Create device-local buffer
            _deviceBuffer = VkBuffer.Create(
                context,
                Capacity * _stride,
                bufferUsageFlags,
                MemoryPropertyFlags.DeviceLocalBit);
        }

        public void StageData(UploadBatch batch, T[] data, ulong startIndex = 0)
        {
            StageData(batch, data.AsSpan(), startIndex);
        }

        public void StageData(UploadBatch batch, Span<T> data, ulong startIndex = 0)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if ((ulong)data.Length + startIndex > Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(data), "Exceeds buffer capacity");
            }

            var size = (ulong)(Unsafe.SizeOf<T>() * data.Length);
            var offset = startIndex * _stride;

            // Write to staging buffer
            batch.StageToBuffer(data, _deviceBuffer, offset,size);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newCapacity">new capacity to change</param>
        /// <param name="batch">Graphics batch</param>

        public void Resize(ulong newCapacity, UploadBatch batch)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if(batch.SubmitContext.QueueFamily != _context.Device.GraphicsQueue.FamilyIndex)
            {
                throw new InvalidOperationException("Invalid batch sended, Pass the graphics batch");
            }
            if (newCapacity == Capacity)
            {
                return;
            }

            var newSize = newCapacity * _stride;
            var newDeviceBuffer = VkBuffer.Create(
                _context,
                newSize,
                BufferUsageFlags.StorageBufferBit |
                BufferUsageFlags.TransferDstBit |
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit
            );


            // Copy existing data if needed
            if (Capacity > 0 && newCapacity > 0)
            {
                var copySize = Math.Min(Capacity, newCapacity) * _stride;

                // Transition old buffer to transfer source
                var srcBarrier = new BufferMemoryBarrier2
                {
                    SType = StructureType.BufferMemoryBarrier2,
                    SrcStageMask = PipelineStageFlags2.AllCommandsBit,
                    DstStageMask = PipelineStageFlags2.TransferBit,
                    SrcAccessMask = AccessFlags2.MemoryReadBit | AccessFlags2.MemoryWriteBit,
                    DstAccessMask = AccessFlags2.TransferReadBit,
                    Buffer = _deviceBuffer,
                    Offset = 0,
                    Size = copySize
                };
                
                batch.PipelineBarrier(
                    bufferMemoryBarriers: [srcBarrier]
                );

                // Transition new buffer to transfer destination
                var dstBarrier = new BufferMemoryBarrier2
                {
                    SType = StructureType.BufferMemoryBarrier2,
                    SrcStageMask = PipelineStageFlags2.TopOfPipeBit,
                    DstStageMask = PipelineStageFlags2.TransferBit,
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags2.TransferWriteBit,
                    Buffer = newDeviceBuffer,
                    Offset = 0,
                    Size = copySize
                };

                batch.PipelineBarrier(
                    bufferMemoryBarriers: [dstBarrier]
                );

                // Copy data
                var copyRegion = new BufferCopy
                {
                    SrcOffset = 0,
                    DstOffset = 0,
                    Size = copySize
                };

                batch.CopyBuffer(_deviceBuffer, newDeviceBuffer, in copyRegion);

                // Transition new buffer to shader access
                var finalBarrier = new BufferMemoryBarrier2
                {
                    SType = StructureType.BufferMemoryBarrier2,
                    SrcStageMask = PipelineStageFlags2.TransferBit,
                    DstStageMask = PipelineStageFlags2.AllGraphicsBit | PipelineStageFlags2.ComputeShaderBit,
                    SrcAccessMask = AccessFlags2.TransferWriteBit,
                    DstAccessMask = AccessFlags2.ShaderReadBit | AccessFlags2.ShaderWriteBit,
                    Buffer = newDeviceBuffer,
                    Offset = 0,
                    Size = newSize
                };

                batch.PipelineBarrier(
                    bufferMemoryBarriers: [finalBarrier]
                );
            }

            // Dispose old buffers
            batch.AddDependency(_deviceBuffer);

            _deviceBuffer = newDeviceBuffer;
            Capacity = newCapacity;
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
    }
}