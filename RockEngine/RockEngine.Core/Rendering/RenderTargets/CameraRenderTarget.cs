using RockEngine.Core.Helpers.Attributes;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.RenderTargets
{
    public partial class CameraRenderTarget : RenderTarget
    {
        private readonly GBuffer _gBuffer;
        private readonly VulkanContext _context;
        private readonly GraphicsContext _engine;

        public GBuffer GBuffer => _gBuffer;
        public override Viewport Viewport => new Viewport()
        {
            X = 0,
            Y = 0,
            Width = Size.Width,
            Height = Size.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f
        };
        public override Rect2D Scissor => new Rect2D()
        {
            Offset = new Offset2D { X = 0, Y = 0 },
            Extent = Size
        };

        public CameraRenderTarget(VulkanContext context, GraphicsContext engine, Extent2D size)
            : base(context, size, engine.Swapchain.Format, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransientAttachmentBit | ImageUsageFlags.SampledBit)
        {
            _context = context;
            _engine = engine;
            _gBuffer = new GBuffer(context, size, engine.Swapchain.DepthFormat);
            CreateTexture();
            ClearValues =
           [
                // Color attachments (Albedo, Normal, Position)
                new ClearValue { Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 1.0f) },
                new ClearValue { Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 1.0f) },
                new ClearValue { Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 1.0f) },
                new ClearValue { Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 1.0f) },
                //new ClearValue { Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 1.0f) },
        
                // Depth attachment
                new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) },
        
                // Output texture
                new ClearValue { Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f) }
           ];
        }

        public override void Initialize(RckRenderPass renderPass)
        {
            RenderPass = renderPass;
            CreateFramebuffers();
        }

        private void CreateTexture()
        {
            OutputTexture = (Texture2D)new Texture.Builder(_context)
                                    .SetSize(Size)
                                    .SetFormat(Format)
                                    .SetUsage(ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit)
                                    .Build();
            OutputTexture.Image.LabelObject("Camera render target");
        }

        protected override void CreateFramebuffers()
        {
            foreach (var fb in Framebuffers)
            {
                fb?.Dispose();
            }

            Framebuffers = new VkFrameBuffer[_context.MaxFramesPerFlight];

            for (int i = 0; i < Framebuffers.Length; i++)
            {
                var attachments = _gBuffer.ColorAttachments.Concat([_gBuffer.DepthAttachment, OutputTexture.Image.GetMipView(0)]).ToArray();

                Framebuffers[i] = VkFrameBuffer.Create(
                    _context,
                    RenderPass.RenderPass,
                    attachments,
                    Size.Width,
                    Size.Height
                );
                Framebuffers[i].LabelObject($"Cam framebuffer ({i})");
            }
        }

        public override void PrepareForRender(VkCommandBuffer cmd)
        {
            using (cmd.NameAction(nameof(PrepareForRender), [1, 1, 1, 1]))
            {
                OutputTexture.Image.TransitionImageLayout(cmd, ImageLayout.ColorAttachmentOptimal);
            }
        }

        [GPUAction([1, 1, 1, 1])]
        public override void TransitionToRead(VkCommandBuffer cmd)
        {
            using (cmd.NameAction(nameof(TransitionToRead), [1, 1, 1, 1]))
            {
                OutputTexture.Image.TransitionImageLayout(cmd, ImageLayout.ShaderReadOnlyOptimal);
            }
        }

        public override void Resize(Extent2D newSize)
        {
            if (newSize.Width == Size.Width && newSize.Height == Size.Height)
            {
                return;
            }
            base.Resize(newSize);
            _gBuffer.Recreate(Size);
            CreateTexture();
            CreateFramebuffers();

        }
        protected override void DisposeResources()
        {
            _context.GraphicsSubmitContext.AddDependency(OutputTexture);
        }

        
    }
}