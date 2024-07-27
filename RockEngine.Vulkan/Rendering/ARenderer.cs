using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Rendering
{
    public abstract class ARenderer
    {
        protected VulkanContext _context;
        protected readonly ISurfaceHandler _surface;
        protected int CurrentFrameIndex => _swapchain.CurrentFrameIndex;
        protected uint _currentImageIndex;
        protected bool _frameStarted;
        protected CommandBufferWrapper[] _commandBuffers;
        protected SwapchainWrapper _swapchain;

        public SwapchainWrapper Swapchain => _swapchain;

        protected ARenderer(VulkanContext context, ISurfaceHandler surface)
        {
            _context = context;
            _surface = surface;
            CreateCommandBuffers();
            CreateSwapChain(surface);
        }

        public CommandBufferWrapper GetCurrentCommandBuffer() => _commandBuffers[CurrentFrameIndex];
        public RenderPassWrapper GetRenderPass() => _swapchain.RenderPass;
        public FramebufferWrapper GetCurrentFrameBuffer() => _swapchain.SwapchainFramebuffers[_currentImageIndex];
        public int FrameIndex => CurrentFrameIndex;

        protected abstract void CreateCommandBuffers();
        public abstract Task<CommandBufferWrapper?> BeginFrameAsync();
        public abstract void EndFrame();
        public abstract void BeginSwapchainRenderPass(in CommandBufferWrapper commandBuffer);
        public abstract void EndSwapchainRenderPass(in CommandBufferWrapper commandBuffer);

        protected void RecreateSwapChainAsync()
        {
            _swapchain.RecreateSwapchainAsync(_surface, (uint)_surface.Size.X, (uint)_surface.Size.Y);
        }

        protected abstract void CreateSwapChain(ISurfaceHandler surfaceHandler);

        public unsafe void BindDescriptorSets(CommandBufferWrapper commandBuffer, PipelineLayout layout, DescriptorSet[] descriptorSets)
        {
            fixed (DescriptorSet* pDescriptorSets = descriptorSets)
            {
                _context.Api.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, layout, 0, (uint)descriptorSets.Length, pDescriptorSets, 0, null);
            }
        }

        public void BindPipeline(CommandBufferWrapper commandBuffer, Pipeline pipeline)
        {
            _context.Api.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        }

        public void Draw(CommandBufferWrapper commandBuffer, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            _context.Api.CmdDraw(commandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public void DrawIndexed(CommandBufferWrapper commandBuffer, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
        {
            _context.Api.CmdDrawIndexed(commandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        public void SetScissor(CommandBufferWrapper commandBuffer, int offsetX, int offsetY, uint width, uint height)
        {
            var scissor = new Rect2D() { Offset = new Offset2D(offsetX, offsetY), Extent = new Extent2D(width, height) };
            _context.Api.CmdSetScissor(commandBuffer, 0, 1, ref scissor);
        }


        public void SetViewport(CommandBufferWrapper commandBuffer, float width, float height, float minDepth = 0.0f, float maxDepth = 1.0f)
        {
            var viewport = new Viewport() { Width = width, Height = height, MinDepth = minDepth, MaxDepth = maxDepth };
            _context.Api.CmdSetViewport(commandBuffer, 0, 1, ref viewport);
        }
    }
}
