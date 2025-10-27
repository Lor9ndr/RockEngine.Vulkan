using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Editor.Rendering.RenderTargets
{
    public class PickingRenderTarget : RenderTarget
    {
        private readonly Format[] _colorFormats;
        private readonly SampleCountFlags _sampleCount;
        private readonly VulkanContext _context;
        private bool _initilized;
        private Texture2D _depthTexture;

        public PickingRenderTarget(VulkanContext context, Extent2D size)
            : base(context, size, Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit)
        {
            _colorFormats = new[] { Format.R8G8B8A8Unorm };
            _sampleCount = SampleCountFlags.Count1Bit;

            // Create output texture
            OutputTexture = (Texture2D)new Texture.Builder(context)
                .SetSize(size)
                .SetFormat(Format.R8G8B8A8Unorm)
                .SetUsage(ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit)
                .Build();

            OutputTexture.Image.LabelObject("PickingRenderTarget Output");

            var graphicsEngine = IoC.Container.GetInstance<GraphicsEngine>();

            // Create depth texture
            _depthTexture = (Texture2D)new Texture.Builder(context)
                .SetSize(size)
                .SetFormat(graphicsEngine.Swapchain.DepthFormat)
                .SetAspectMask(ImageAspectFlags.DepthBit)
                .SetUsage(ImageUsageFlags.DepthStencilAttachmentBit)
                .Build();
            _depthTexture.Image.LabelObject("PickingRenderTarget DepthOutput");

            _context = context;
        }

        public override void Initialize(RckRenderPass renderPass)
        {
            if (_initilized)
            {
                return;
            }
            RenderPass = renderPass;
            CreateFramebuffers();

            // Setup viewport and scissor
            Viewport = new Viewport(0, 0, Size.Width, Size.Height, 0.0f, 1.0f);
            Scissor = new Rect2D(new Offset2D(0, 0), Size);

            // Setup clear values
            ClearValues = new ClearValue[]
            {
                new ClearValue { Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 0.0f) }, // Clear to black (ID 0)
                new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
            };
            _initilized = true;
        }

        protected override void CreateFramebuffers()
        {
            for (int i = 0; i < Context.MaxFramesPerFlight; i++)
            {
                var attachments = new[]
                 {
                    OutputTexture.Image.GetOrCreateView(ImageAspectFlags.ColorBit),
                    _depthTexture.Image.GetOrCreateView(ImageAspectFlags.DepthBit)
                };

                Framebuffers[i] = VkFrameBuffer.Create(
                    Context,
                    RenderPass.RenderPass,
                    attachments,
                    OutputTexture.Width,
                    OutputTexture.Height);
            }
        }

        public override void PrepareForRender(VkCommandBuffer cmd)
        {
            // Transition color attachment to color attachment optimal
         /*   if (OutputTexture.Image.GetMipLayout(0) != ImageLayout.ColorAttachmentOptimal)
            {
                OutputTexture.Image.TransitionImageLayout(cmd, ImageLayout.ColorAttachmentOptimal);
            }*/
        }

        public override void TransitionToRead(VkCommandBuffer cmd)
        {
            // Transition color attachment to transfer source for reading
             /*OutputTexture.Image.TransitionImageLayout(cmd, ImageLayout.TransferSrcOptimal);*/
        }

        protected override void DisposeResources()
        {
 
            foreach (var framebuffer in Framebuffers)
            {
                framebuffer?.Dispose();
            }
            _context.GraphicsSubmitContext.AddDependency(OutputTexture);
            _context.GraphicsSubmitContext.AddDependency(_depthTexture);
        }

        public override void Resize(Extent2D newSize)
        {
            if (Size.Width == newSize.Width && Size.Height == newSize.Height)
            {
                return;
            }

            if (!_initilized)
            {
                return;
            }

            base.Resize(newSize);

            // Recreate output texture with new size
            OutputTexture?.Dispose();
            OutputTexture = (Texture2D?)new Texture.Builder(Context)
                .SetSize(newSize)
                .SetFormat(Format.R8G8B8A8Unorm)
                .SetUsage(ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit)
                .Build();
            _depthTexture = (Texture2D)new Texture.Builder(Context)
              .SetSize(newSize)
              .SetFormat(_depthTexture.Image.Format)
              .SetAspectMask(ImageAspectFlags.DepthBit)
              .SetUsage(ImageUsageFlags.DepthStencilAttachmentBit)
              .Build();

            CreateFramebuffers();
        }
    }
}