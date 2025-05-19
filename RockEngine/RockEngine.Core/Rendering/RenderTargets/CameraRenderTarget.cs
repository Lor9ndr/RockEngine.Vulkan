using RockEngine.Core.Extensions.Builders;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.RenderTargets
{
    public class CameraRenderTarget : RenderTarget
    {
        private readonly GBuffer _gBuffer;
        private readonly VulkanContext _context;
        private readonly GraphicsEngine _engine;

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

        public CameraRenderTarget(VulkanContext context, GraphicsEngine engine, Extent2D size, EngineRenderPass deferredRenderPass)
            : base(context, size, engine.Swapchain.Format, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransientAttachmentBit | ImageUsageFlags.SampledBit)
        {
            _context = context;
            _engine = engine;
            _gBuffer = new GBuffer(context, size, engine.Swapchain.DepthFormat);
            CreateTexture();
            RenderPass = deferredRenderPass;
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

        private void CreateTexture()
        {
            OutputTexture = new Texture.Builder(_context)
                                    .SetSize(Size)
                                    .SetFormat(Format)
                                    .SetUsage(ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit)
                                    .Build();
            OutputTexture.Image.LabelObject("Camera render target");
        }

        public override void CreateFramebuffers()
        {
            foreach (var fb in Framebuffers)
            {
                fb?.Dispose();
            }

            Framebuffers = new VkFrameBuffer[_context.MaxFramesPerFlight];

            for (int i = 0; i < Framebuffers.Length; i++)
            {
                var attachments = _gBuffer.ColorAttachments.Concat([_gBuffer.DepthAttachment, OutputTexture.ImageView]).ToArray();

                Framebuffers[i] = VkFrameBuffer.Create(
                    _context,
                    RenderPass,
                    attachments,
                    Size.Width,
                    Size.Height
                );
                Framebuffers[i].LabelObject($"Cam framebuffer ({i})");
            }
        }

        public override void PrepareForRender(VkCommandBuffer cmd)
        {
            using (cmd.NameAction("Transition camera output to ColorAttachmentOptimal", [1, 1, 1, 1]))
            {
                OutputTexture.Image.TransitionImageLayout(cmd, ImageLayout.ColorAttachmentOptimal);
            }
        }

        public override void TransitionToRead(VkCommandBuffer cmd)
        {
            using (cmd.NameAction("Transition camera output to ShaderReadOnlyOptimal", [1, 1, 1, 1]))
            {
                OutputTexture.Image.TransitionImageLayout(cmd, ImageLayout.ShaderReadOnlyOptimal);
            }
        }

        public override void Resize(Extent2D newSize)
        {
            base.Resize(newSize);
            _gBuffer.Recreate(Size);
            CreateTexture();
            CreateFramebuffers();
        }
        protected override void DisposeResources()
        {
            OutputTexture?.Dispose();
        }
    }
}