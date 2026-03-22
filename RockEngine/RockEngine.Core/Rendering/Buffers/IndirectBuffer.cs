using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Buffers
{
    public sealed class IndirectBuffer : IDisposable
    {
        private readonly VulkanContext _context;
        private VkBuffer _deviceBuffer;
        private bool _disposed;
        private ulong _capacity;

        public VkBuffer Buffer => _deviceBuffer;
        public ulong Capacity => _capacity;
        public ulong Stride { get; }

        public IndirectBuffer(VulkanContext context, ulong initialCapacity)
        {
            _context = context;
            _capacity = initialCapacity;
            Stride = (ulong)Unsafe.SizeOf<DrawIndexedIndirectCommand>();
            CreateDeviceBuffer();
        }

        private void CreateDeviceBuffer()
        {
            _deviceBuffer = VkBuffer.Create(
                _context,
                _capacity * Stride,
                BufferUsageFlags.IndirectBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit);
        }

        /// <summary>
        /// Resizes the buffer to a new capacity. The copy from the old buffer is added to the given batch.
        /// The old buffer will be disposed after the batch is submitted and finished.
        /// </summary>
        /// <param name="batch">The batch to add the copy operation to.</param>
        /// <param name="newCapacity">New capacity in number of commands.</param>
        public void Resize(UploadBatch batch, ulong newCapacity)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (newCapacity == _capacity)
                return;

            var oldBuffer = _deviceBuffer;
            var oldSize = _capacity * Stride;

            _capacity = newCapacity;
            CreateDeviceBuffer();

            // Copy existing data from old buffer to new buffer
            if (oldSize > 0)
            {
                batch.CopyBuffer(oldBuffer, _deviceBuffer, 0, 0, oldSize);
            }

            // Schedule the old buffer for disposal after the batch completes
            batch.AddDependency(oldBuffer);
        }

        /// <summary>
        /// Adds commands to the batch, copying from staging to the device buffer.
        /// Assumes the buffer has enough capacity (offset + commands size ≤ capacity).
        /// </summary>
        public void StageCommands(UploadBatch batch, ReadOnlySpan<DrawIndexedIndirectCommand> commands, ulong offset = 0)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            ulong requiredSize = (ulong)(Unsafe.SizeOf<DrawIndexedIndirectCommand>() * commands.Length);
            if (offset + requiredSize > _capacity * Stride)
                throw new InvalidOperationException("Indirect buffer does not have enough capacity for the commands. Resize first.");

            if (!batch.StagingManager.TryStage<DrawIndexedIndirectCommand>(batch, commands, out var stageOffset, out var stagedSize))
                throw new InvalidOperationException("Failed to stage indirect commands");

            batch.CopyBuffer(batch.StagingManager.StagingBuffer, _deviceBuffer, stageOffset, offset, requiredSize);
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