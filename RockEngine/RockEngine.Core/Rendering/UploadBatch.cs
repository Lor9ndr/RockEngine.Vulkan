using RockEngine.Core.Rendering.Contexts;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public sealed class UploadBatch : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly StagingManager _stagingManager;
        private readonly VkCommandPool _pool;
        private VkCommandBuffer _commandBuffer;
        private bool _isDisposed;

        public VkCommandBuffer CommandBuffer => _commandBuffer;

        public UploadBatch(VulkanContext context, StagingManager stagingManager, VkCommandPool pool)
        {
            _context = context;
            _stagingManager = stagingManager;
            _pool = pool;
            AllocateCommandBuffer();
        }

        private void AllocateCommandBuffer()
        {
            _commandBuffer = _pool.AllocateCommandBuffer(CommandBufferLevel.Primary);
            BeginCommandBuffer();
        }

        private void BeginCommandBuffer()
        {
            _commandBuffer.Begin(new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            });
        }

        public void Reset()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException("UploadBatch");
            }

            // Явный сброс буфера команд
            _commandBuffer.Reset(CommandBufferResetFlags.None);
            BeginCommandBuffer();
        }

        public unsafe void StageToBuffer<T>(
            T[] data,
            VkBuffer destination,
            ulong dstOffset,
            ulong size) where T : unmanaged
        {
            if (!_stagingManager.TryStage(data, out var srcOffset, out var stagedSize))
            {
                throw new InvalidOperationException("Staging buffer overflow");
            }

            var copy = new BufferCopy
            {
                SrcOffset = srcOffset,
                DstOffset = dstOffset,
                Size = size
            };

            _commandBuffer.CopyBuffer(
                _stagingManager.StagingBuffer,
                destination,
                copy
            );
        }

        public void Submit(SubmitContext submitContext)
        {
            _commandBuffer.End();
            submitContext.AddSubmission(_commandBuffer);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            // Не освобождаем буфер команд, только помечаем как доступный для повторного использования
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        // Для безопасного освобождения ресурсов при уничтожении пула
        internal void ForceDispose()
        {
            if (!_isDisposed)
            {
                _commandBuffer.Dispose();
                _isDisposed = true;
            }
        }
    }
}