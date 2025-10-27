using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
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
        public RckRenderPass RenderPass { get; protected set; }
        public Texture2D OutputTexture { get; protected set; }
        public virtual Viewport Viewport { get; protected set; }
        public virtual Rect2D Scissor { get; protected set; }
        public ClearValue[] ClearValues { get; protected set; }
        public Material? Material { get;set;}

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
        public abstract void Initialize(RckRenderPass renderPass);
        

        public virtual void Resize(Extent2D newSize)
        {
            Size = newSize;
            Scissor = new Rect2D(new Offset2D(0,0), newSize);
            Viewport = new Viewport(0,0, newSize.Width,newSize.Height,0,1);
            DisposeResources();
        }

        protected virtual void DisposeResources()
        {

        }


        public void Dispose() => DisposeResources();
    }
}