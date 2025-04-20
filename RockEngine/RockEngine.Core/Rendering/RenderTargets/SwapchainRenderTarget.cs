using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.RenderTargets
{
    public class SwapchainRenderTarget : RenderTarget, IDisposable
    {
        private VkSwapchain _swapchain;

        public SwapchainRenderTarget(VulkanContext context, VkSwapchain swapchain)
            : base(context, swapchain.Extent, swapchain.Format, ImageUsageFlags.ColorAttachmentBit)
        {
            _swapchain = swapchain;
            _swapchain.OnSwapchainRecreate += HandleSwapchainRecreated;
            InitializeResources();
        }

        private void InitializeResources()
        {
            RenderPass = CreateRenderPass();
            CreateFramebuffers();

            ClearValues =
            [
                new ClearValue { Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f) },
                new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
            ];

            UpdateViewportAndScissor();
        }

        private void HandleSwapchainRecreated(VkSwapchain newSwapchain)
        {

            // Update to new swapchain and resubscribe
            _swapchain = newSwapchain;

            // Recreate resources with new swapchain
            RecreateResources();
        }

        private void RecreateResources()
        {
            // Dispose old resources
            foreach (var fb in Framebuffers)
            {
                fb?.Dispose();
            }
            RenderPass?.Dispose();

            // Update dimensions
            Size = _swapchain.Extent;
            Format = _swapchain.Format;

            // Recreate resources
            RenderPass = CreateRenderPass();
            CreateFramebuffers();
            UpdateViewportAndScissor();
        }

        private void UpdateViewportAndScissor()
        {
            Viewport = new Viewport
            {
                X = 0,
                Y = 0,
                Width = Size.Width,
                Height = Size.Height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };

            Scissor = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = Size
            };
        }

        private VkRenderPass CreateRenderPass()
        {
            var builder = new RenderPassBuilder(Context)
               .ConfigureAttachment(Format)
                   .WithColorOperations(
                       load: AttachmentLoadOp.Clear,
                       store: AttachmentStoreOp.Store,
                       initialLayout: ImageLayout.Undefined,
                       finalLayout: ImageLayout.PresentSrcKhr)
                   .Add()
               .ConfigureAttachment(_swapchain.DepthFormat)
                   .WithDepthOperations(
                       load: AttachmentLoadOp.Clear,
                       store: AttachmentStoreOp.Store,
                       initialLayout: ImageLayout.Undefined,
                       finalLayout: ImageLayout.DepthStencilAttachmentOptimal)
           .Add();

            // Single subpass for color and depth
            builder.BeginSubpass()
                .AddColorAttachment(0)
                .SetDepthAttachment(1)
                .EndSubpass();

            return builder.Build();
        }

        public override void CreateFramebuffers()
        {
            Framebuffers = new VkFrameBuffer[_swapchain.SwapChainImagesCount];
            for (int i = 0; i < _swapchain.SwapChainImagesCount; i++)
            {
                var attachments = new VkImageView[]
                {
                    _swapchain.SwapChainImageViews[i],
                    _swapchain.DepthImageView
                };

                Framebuffers[i] = VkFrameBuffer.Create(
                    Context,
                    RenderPass,
                    attachments,
                    Size.Width,
                    Size.Height
                );
            }
        }

        public override void PrepareForRender(VkCommandBuffer cmd)
        {
            // Automatic layout transitions handled by render pass
        }

        public override void TransitionToRead(VkCommandBuffer cmd)
        {
            // No explicit transitions needed
        }

        protected override void DisposeResources()
        {
            _swapchain.OnSwapchainRecreate -= HandleSwapchainRecreated;
            foreach (var fb in Framebuffers)
            {
                fb?.Dispose();
            }
            RenderPass?.Dispose();
        }
    }
}