using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class GraphicsEngine : IDisposable
    {
        private readonly VulkanContext _renderingContext;
        private readonly VkCommandPool _commandBufferPool;
        private readonly VkSwapchain _swapchain;
        private readonly RenderPassManager _renderPassManager;

        public VkSwapchain Swapchain => _swapchain;

        public RenderPassManager RenderPassManager => _renderPassManager;

        public uint CurrentImageIndex => _swapchain.CurrentImageIndex;


        public GraphicsEngine(VulkanContext renderingContext)
        {
            _renderingContext = renderingContext;
            
            _swapchain = VkSwapchain.Create(_renderingContext, _renderingContext.Surface);
            _renderPassManager = new RenderPassManager(_renderingContext);
        }


        public UploadBatch? Begin()
        {
            if (_swapchain.Surface.Size.X == 0 || _swapchain.Surface.Size.Y == 0)
            {
                return null;
            }

            var result = _swapchain.AcquireNextImage()
               .VkAssertResult("Failed to acquire swap chain image!", Result.SuboptimalKhr, Result.ErrorOutOfDateKhr);

            if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapchain();
                return null;
            }
            var batch = _renderingContext.SubmitContext.CreateBatch();
            batch.CommandBuffer.LabelObject("GraphicsEngine cmd");
            return batch;
            
        }

        public void SubmitAndPresent(UploadBatch batch)
        {
            var data = _swapchain.GetFrameData();
            _renderingContext.SubmitContext.AddSignalSemaphore(data.RenderFinishedSemaphore);
            batch.Submit();

            _renderingContext.SubmitContext.AddWaitSemaphore(data.ImageAvailableSemaphore, PipelineStageFlags.ColorAttachmentOutputBit);

             var operation = _renderingContext.SubmitContext.FlushAsync(data.InFlightFence);
            _swapchain.Present(operation);
        }

      

        private void RecreateSwapchain()
        {
            _swapchain.RecreateSwapchain();
        }


        public void Dispose()
        {
            _commandBufferPool.Dispose();
            _swapchain.Dispose();
        }
    }
}