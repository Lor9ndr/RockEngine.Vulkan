using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core
{
    public sealed class StorageBuffer<T> : IDisposable where T : unmanaged
    {
        private readonly VulkanContext _context;
        private readonly VkBuffer _deviceBuffer;
        private readonly ulong _stride;
        private bool _disposed;

        public VkBuffer Buffer => _deviceBuffer;
        public ulong Capacity { get; }
        public ulong Stride => _stride;

        public StorageBuffer(VulkanContext context, ulong capacity)
        {
            _context = context;
            Capacity = capacity;

            var elementSize = (ulong)Unsafe.SizeOf<T>();
            var alignment = context.Device.PhysicalDevice.Properties.Limits.MinStorageBufferOffsetAlignment;
            _stride = (elementSize + alignment - 1) & ~(alignment - 1);

            _deviceBuffer = VkBuffer.Create(
                context,
                Capacity * _stride,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit);
        }

        public void StageData(UploadBatch batch, T[] data, ulong startIndex = 0)
        {
            if ((ulong)data.Length + startIndex > Capacity)
                throw new ArgumentOutOfRangeException(nameof(data), "Exceeds buffer capacity");

            batch.StageToBuffer(
                data,
                _deviceBuffer,
                startIndex * _stride,
                (ulong)(Unsafe.SizeOf<T>() * data.Length)
            );
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _deviceBuffer.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}