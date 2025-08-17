using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System;
using System.Runtime.CompilerServices;

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
                BufferUsageFlags.IndirectBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit);
        }

        public void StageCommands(UploadBatch batch, DrawIndexedIndirectCommand[] commands, ulong offset = 0)
        {
            StageCommands(batch, commands.AsSpan(), offset);
        }

        public void StageCommands(UploadBatch batch, Span<DrawIndexedIndirectCommand> commands, ulong offset = 0)
        {
            ulong size = (ulong)(Unsafe.SizeOf<DrawIndexedIndirectCommand>() * commands.Length);

            // Убедимся, что буфер достаточно большой
            if (size > _deviceBuffer.Size)
            {
                Resize(size * 2);
            }

            // Запись данных в стаджинг буфер
            if (!batch.SubmitContext.StagingManager.TryStage(batch, commands, out var stageOffset, out var stagedSize))
            {
                throw new InvalidOperationException("Failed to stage indirect commands");
            }

            // Копирование из стаджинг буфера в GPU буфер
            var copyRegion = new BufferCopy
            {
                SrcOffset = stageOffset,
                DstOffset = 0,
                Size = size
            };

            VulkanContext.Vk.CmdCopyBuffer(
                batch.CommandBuffer,
                _context.SubmitContext.StagingManager.StagingBuffer,
                _deviceBuffer,
                1,
                in copyRegion
            );

        }

        public void Resize(ulong newCapacity)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var oldBuffer = _deviceBuffer;
            _capacity = newCapacity;
            CreateDeviceBuffer();

            // Copy old data if needed
            // (Implement using a temporary UploadBatch if necessary)
            oldBuffer.Dispose();
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