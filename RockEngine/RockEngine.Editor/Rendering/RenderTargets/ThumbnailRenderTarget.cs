using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Editor.Thumbnails
{
    public class ThumbnailRenderTarget : IRenderTarget
    {
        private readonly VulkanContext _context;
        private readonly uint _width;
        private readonly uint _height;

        public Extent2D Size { get; private set; }
        public Format Format { get; private set; }
        public VkFrameBuffer[] Framebuffers { get; private set; }
        public RckRenderPass RenderPass { get; private set; }
        public Viewport Viewport { get; }
        public Rect2D Scissor { get; }
        public ClearValue[] ClearValues { get; }

        // Color and depth images
        public VkImage ColorImage { get; private set; }
        public VkImageView ColorImageView { get; private set; }
        public VkImage DepthImage { get; private set; }
        public VkImageView DepthImageView { get; private set; }

        public ThumbnailRenderTarget(VulkanContext context, uint width = 128, uint height = 128)
        {
            _context = context;
            _width = width;
            _height = height;
            Size = new Extent2D(width, height);
            Format = Format.R8G8B8A8Unorm;

            // Create color image using factory method
            ColorImage = VkImage.Create(
                context,
                width, height,
                Format,
                ImageTiling.Optimal,
                ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit,
                ImageLayout.Undefined,
                1, 1, SampleCountFlags.Count1Bit,
                ImageAspectFlags.ColorBit
            );
            ColorImage.LabelObject("ThumbnailColor");
            ColorImageView = ColorImage.GetOrCreateView(ImageAspectFlags.ColorBit);

            // Create depth image
            var depthFormat = context.Device.PhysicalDevice.FindDepthFormat();
            DepthImage = VkImage.Create(
                context,
                width, height,
                depthFormat,
                ImageTiling.Optimal,
                ImageUsageFlags.DepthStencilAttachmentBit,
                MemoryPropertyFlags.DeviceLocalBit,
                ImageLayout.Undefined,
                1, 1, SampleCountFlags.Count1Bit,
                ImageAspectFlags.DepthBit
            );
            DepthImage.LabelObject("ThumbnailDepth");
            DepthImageView = DepthImage.GetOrCreateView(ImageAspectFlags.DepthBit);

            ClearValues = new[]
            {
                new ClearValue { Color = new ClearColorValue(0, 0, 0, 0) },
                new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
            };

            Viewport = new Viewport
            {
                X = 0,
                Y = 0,
                Width = width,
                Height = height,
                MinDepth = 0,
                MaxDepth = 1
            };
            Scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height));
        }

        public void Initialize(RckRenderPass renderPass)
        {
            RenderPass = renderPass;
            CreateFramebuffers();
        }

        private void CreateFramebuffers()
        {
            var attachments = new[] { ColorImageView, DepthImageView };
            Framebuffers = new VkFrameBuffer[1];
            Framebuffers[0] = VkFrameBuffer.Create(
                _context,
                RenderPass.RenderPass,
                attachments,
                _width,
                _height
            );
            Framebuffers[0].LabelObject("ThumbnailFramebuffer");
        }

        public void PrepareForRender(UploadBatch batch)
        {
            ColorImage.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal);
            DepthImage.TransitionImageLayout(batch, ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal);
        }

        public void TransitionToRead(UploadBatch batch)
        {
            ColorImage.TransitionImageLayout(batch, ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal);
        }

        public void Dispose()
        {
            Framebuffers?.FirstOrDefault()?.Dispose();
            RenderPass?.Dispose();
            ColorImageView?.Dispose();
            ColorImage?.Dispose();
            DepthImageView?.Dispose();
            DepthImage?.Dispose();
        }
    }
}