using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.RenderTargets
{
    public abstract class RenderTarget : IDisposable, IRenderTarget
    {
        public Extent2D Size { get; protected set; }
        public Format Format { get; protected set; }
        public VkFrameBuffer[] Framebuffers { get; protected set; }
        public VkRenderPass RenderPass { get; protected set; }
        public Texture OutputTexture { get; protected set; }
        public virtual Viewport Viewport { get; protected set; }
        public virtual Rect2D Scissor { get; protected set; }
        public ClearValue[] ClearValues { get; protected set; }
        protected VulkanContext Context { get; }

        protected readonly ImageUsageFlags _usageFlags;
        protected VkFrameBuffer _framebuffer;

        protected RenderTarget(VulkanContext context, Extent2D size, Format format, ImageUsageFlags usageFlags)
        {
            Context = context;
            Size = size;
            Format = format;
            _usageFlags = usageFlags;
            Framebuffers = new VkFrameBuffer[context.MaxFramesPerFlight];
        }

        public abstract void PrepareForRender(VkCommandBuffer cmd);
        public abstract void TransitionToRead(VkCommandBuffer cmd);
        protected abstract void CreateFramebuffers();
        public abstract void Initialize(VkRenderPass renderPass);
        

        public virtual void Resize(Extent2D newSize)
        {
            Size = newSize;
            DisposeResources();
        }

        protected virtual void DisposeResources()
        {

        }


        public void Dispose() => DisposeResources();
    }
}