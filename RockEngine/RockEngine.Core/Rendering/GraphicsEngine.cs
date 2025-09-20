using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class GraphicsEngine : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly VkSwapchain _swapchain;
        private uint _frameCount = 0;

        public uint FrameIndex => (uint)(_frameCount % _swapchain.SwapChainImagesCount);
        public VkSwapchain Swapchain => _swapchain;

        public GraphicsEngine(VulkanContext context)
        {
            _context = context;
            _swapchain = VkSwapchain.Create(context, context.Surface);
        }

        public UploadBatch? Begin()
        {
            var size = _swapchain.Surface.Size;
            if (size.X < 1 || size.Y < 1) return null;


            var frameData = _swapchain.GetFrameData(FrameIndex);
            frameData.FlushOperation?.Wait();

            var result = _swapchain.AcquireNextImage(FrameIndex);
            if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapchain();
                return null;
            }

            var batch = _context.GraphicsSubmitContext.CreateBatch();
            return batch;
        }

        public void SubmitAndPresent(UploadBatch batch)
        {
            
            var frameData = _swapchain.GetFrameData(FrameIndex);

            batch.AddWaitSemaphore(frameData.ImageAvailableSemaphore, PipelineStageFlags.ColorAttachmentOutputBit);
            batch.AddSignalSemaphore(frameData.RenderFinishedSemaphore);
            batch.Submit();

            // Сбрасываем забор перед использованием
            frameData.InFlightFence.Reset();

            // Отправляем команды и ждем завершения
            var operation = _context.GraphicsSubmitContext.FlushAsync(frameData.InFlightFence);

            // Представляем кадр
            var result = _swapchain.Present(FrameIndex, operation);

            _frameCount++;
        }

        private void RecreateSwapchain()
        {
            _swapchain.RecreateSwapchain();
            _frameCount = 0;
        }

        public void Dispose() => _swapchain.Dispose();
    }
}