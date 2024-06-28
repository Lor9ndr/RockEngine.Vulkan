using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Diagnostics;

namespace RockEngine.Vulkan.Rendering
{
    internal class BaseRenderer : ARenderer, IDisposable
    {
        public event Action<CommandBufferWrapper>? AfterRenderPass;
        public event Action<CommandBufferWrapper>? BeforeRenderPass;
        public event Action<CommandBufferWrapper>? FrameEnded;
        public event Action<CommandBufferWrapper>? FrameStarted;

        public BaseRenderer(VulkanContext context, ISurfaceHandler surfaceHandler)
            : base(context, surfaceHandler)
        {
        }

        protected override void CreateSwapChain(ISurfaceHandler surfaceHandler)
        {
            _swapchain = SwapchainWrapper.Create(_context, surfaceHandler, (uint)surfaceHandler.Size.X, (uint)surfaceHandler.Size.Y);
        }

        protected override void CreateCommandBuffers()
        {
            var commandPool = _context.GetOrCreateCommandPool();
            var allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                CommandBufferCount = 1,
                Level = CommandBufferLevel.Primary
            };
            _commandBuffers = new CommandBufferWrapper[VulkanContext.MAX_FRAMES_IN_FLIGHT];
            for (int i = 0; i < _commandBuffers.Length; i++)
            {
                _commandBuffers[i] = CommandBufferWrapper.Create(_context, in allocInfo, commandPool);
            }
        }

        public override CommandBufferWrapper? BeginFrame()
        {

            float width = _surface.Size.X, height = _surface.Size.Y;
            if (width == 0 || height == 0)
            {

                return null; // Skip rendering if the window is minimized
            }

            var commandBuffer = GetCurrentCommandBuffer();

            var result = _swapchain.AcquireNextImage(ref _currentImageIndex)
                    .ThrowCode("Failed to acquire swap chain image!", Result.SuboptimalKhr, Result.ErrorOutOfDateKhr);

            if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapChainAsync();
                return null;
            }

            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.None,
                PInheritanceInfo = default // Only relevant for secondary command buffers
            };

            _context.Api.BeginCommandBuffer(commandBuffer, in beginInfo)
                .ThrowCode("Failed to begin recording command buffer!");

            _frameStarted = true;
            FrameStarted?.Invoke(commandBuffer);
            return commandBuffer;

        }

        public override void EndFrame()
        {
            var commandBuffer = GetCurrentCommandBuffer();

            _context.Api.EndCommandBuffer(commandBuffer)
                .ThrowCode("Failed to end recording command buffer!");

            _swapchain.SubmitCommandBuffers(new[] { commandBuffer.VkObjectNative }, _currentImageIndex);
            _frameStarted = false;

            _currentFrameIndex = (_currentFrameIndex + 1) % VulkanContext.MAX_FRAMES_IN_FLIGHT;
            FrameEnded?.Invoke(commandBuffer);
        }

        public unsafe override void BeginSwapchainRenderPass(CommandBufferWrapper commandBuffer)
        {
            BeforeRenderPass?.Invoke(commandBuffer);

            var viewport = new Viewport() { Width = _swapchain.Extent.Width, Height = _swapchain.Extent.Height, MaxDepth = 1.0f };
            var scissor = new Rect2D() { Extent = _swapchain.Extent };

            _context.Api.CmdSetViewport(commandBuffer, 0, 1, ref viewport);
            _context.Api.CmdSetScissor(commandBuffer, 0, 1, ref scissor);

            ClearValue[] cv = {
                new ClearValue(color: new ClearColorValue() { Float32_0 = 0.2f, Float32_1 = 0.2f, Float32_2 = 0.3f, Float32_3 = 1 }),
                new ClearValue(depthStencil: new ClearDepthStencilValue(1.0f, 0))
            };
            fixed (ClearValue* pCV = cv)
            {
                var renderPassInfo = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _swapchain.RenderPass,
                    Framebuffer = _swapchain.SwapchainFramebuffers[_currentImageIndex],
                    RenderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = _swapchain.Extent },
                    ClearValueCount = (uint)cv.Length,
                    PClearValues = pCV
                };
                _context.Api.CmdBeginRenderPass(commandBuffer, in renderPassInfo, SubpassContents.Inline);
            }
        }

        public override void EndSwapchainRenderPass(CommandBufferWrapper commandBuffer)
        {
            Debug.Assert(commandBuffer == GetCurrentCommandBuffer(), "Can't end render pass on command buffer from a different frame");
            Debug.Assert(_frameStarted, "Can't call endSwapChainRenderPass if frame is not in progress");
            _context.Api.CmdEndRenderPass(commandBuffer);

            AfterRenderPass?.Invoke(commandBuffer);
        }

        public void Dispose()
        {
            _swapchain.Dispose();
        }
    }
}
