using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.Rendering
{
    /// <summary>
    /// Abstract base class for rendering systems in the Vulkan engine.
    /// This class provides common functionality for managing command buffers and swapchains.
    /// </summary>
    public abstract class ARenderer
    {
        /// <summary>
        /// The Vulkan context used for rendering operations.
        /// </summary>
        protected VulkanContext _context;

        /// <summary>
        /// The surface handler for managing the rendering surface.
        /// </summary>
        protected readonly ISurfaceHandler _surface;

        /// <summary>
        /// Gets the index of the current frame.
        /// </summary>
        protected int CurrentFrameIndex => _swapchain.CurrentFrameIndex;

        /// <summary>
        /// The index of the current image in the swapchain.
        /// </summary>
        protected uint _currentImageIndex;
        /// <summary>
        /// Indicates whether the frame has started.
        /// </summary>
        protected bool _frameStarted;

        /// <summary>
        /// Array of command buffers for rendering.
        /// </summary>
        protected CommandBufferWrapper[] _commandBuffers;

        /// <summary>
        /// The swapchain used for presenting images.
        /// </summary>
        protected SwapchainWrapper _swapchain; 

        /// <summary>
        /// Gets the swapchain associated with this renderer.
        /// </summary>
        public SwapchainWrapper Swapchain => _swapchain;

        /// <summary>
        /// Initializes a new instance of the <see cref="ARenderer"/> class.
        /// </summary>
        /// <param name="context">The Vulkan context.</param>
        /// <param name="surface">The surface handler.</param>
        protected ARenderer(VulkanContext context, ISurfaceHandler surface)
        {
            _context = context; // Assign the Vulkan context.
            _surface = surface; // Assign the surface handler.
            CreateCommandBuffers(); // Create command buffers for rendering.
            CreateSwapChain(surface); // Create the swapchain for rendering.
        }

        /// <summary>
        /// Gets the current command buffer for rendering.
        /// </summary>
        /// <returns>The current command buffer.</returns>
        public CommandBufferWrapper GetCurrentCommandBuffer() => _commandBuffers[CurrentFrameIndex];

        /// <summary>
        /// Gets the render pass associated with the swapchain.
        /// </summary>
        /// <returns>The render pass.</returns>
        public RenderPassWrapper GetRenderPass() => _swapchain.RenderPass;

        /// <summary>
        /// Gets the current framebuffer for rendering.
        /// </summary>
        /// <returns>The current framebuffer.</returns>
        public FramebufferWrapper GetCurrentFrameBuffer() => _swapchain.SwapchainFramebuffers[_currentImageIndex];

        /// <summary>
        /// Gets the index of the current frame.
        /// </summary>
        public int FrameIndex => CurrentFrameIndex;

        /// <summary>
        /// Creates command buffers for rendering. Must be implemented by derived classes.
        /// </summary>
        protected abstract void CreateCommandBuffers();

        /// <summary>
        /// Begins the rendering frame. Must be implemented by derived classes.
        /// </summary>
        /// <returns>The command buffer for the current frame.</returns>
        public abstract FrameInfo BeginFrame();

        /// <summary>
        /// Ends the rendering frame. Must be implemented by derived classes.
        /// </summary>
        public abstract void EndFrame();

        /// <summary>
        /// Begins the swapchain render pass.
        /// </summary>
        /// <param name="commandBuffer">The command buffer to use for rendering.</param>
        public abstract void BeginSwapchainRenderPass(in CommandBufferWrapper commandBuffer);

        /// <summary>
        /// Ends the swapchain render pass.
        /// </summary>
        /// <param name="commandBuffer">The command buffer to use for rendering.</param>
        public abstract void EndSwapchainRenderPass(in CommandBufferWrapper commandBuffer);

        /// <summary>
        /// Recreates the swapchain asynchronously.
        /// </summary>
        protected void RecreateSwapChainAsync()
        {
            _swapchain.RecreateSwapchainAsync(_surface, (uint)_surface.Size.X, (uint)_surface.Size.Y);
        }

        /// <summary>
        /// Creates the swapchain. Must be implemented by derived classes.
        /// </summary>
        /// <param name="surfaceHandler">The surface handler to use for the swapchain.</param>
        protected abstract void CreateSwapChain(ISurfaceHandler surfaceHandler);
    }
}
