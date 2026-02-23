using RockEngine.Core.Builders;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.RenderTargets
{
    public class SwapchainRenderTarget : RenderTarget, IDisposable
    {
        private VkSwapchain _swapchain;

        public VkSwapchain Swapchain => _swapchain;

        public SwapchainRenderTarget(VulkanContext context, VkSwapchain swapchain)
            : base(context, swapchain.Extent, swapchain.Format, ImageUsageFlags.ColorAttachmentBit)
        {
            _swapchain = swapchain;
            _swapchain.OnSwapchainRecreate += HandleSwapchainRecreated;
        }

        private void InitializeResources()
        {
            CreateFramebuffers();

            ClearValues =new Memory<ClearValue>(
                [ 
                new ClearValue { Color = new ClearColorValue(0) },
                new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
                ]);
            

            UpdateViewportAndScissor();
        }
        public override void Initialize(RckRenderPass renderPass)
        {
            RenderPass = renderPass;
            InitializeResources();
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
                if(fb is not null)
                {
                    Context.GraphicsSubmitContext.AddDependency(fb);
                }
            }

            // Update dimensions
            Size = _swapchain.Extent;
            Format = _swapchain.Format;

            // Recreate resources
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


        protected override void CreateFramebuffers()
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
                    RenderPass.RenderPass,
                    attachments,
                    Size.Width,
                    Size.Height
                );
            }
        }

        public override void PrepareForRender(UploadBatch batch)
        {
            // Automatic layout transitions handled by render pass
        }

        public override void TransitionToRead(UploadBatch batch)
        {
            // No explicit transitions needed
        }

        protected override void DisposeResources()
        {
            _swapchain.OnSwapchainRecreate -= HandleSwapchainRecreated;
            foreach (var fb in Framebuffers)
            {
                Context.GraphicsSubmitContext.AddDependency(fb);
            }
            Framebuffers = [];
            //RenderPass?.Dispose();
        }
    }
}