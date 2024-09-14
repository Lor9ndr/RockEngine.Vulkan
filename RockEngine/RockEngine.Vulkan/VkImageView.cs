
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkImageView : VkObject<ImageView>
    {
        private readonly RenderingContext _context;

        private VkImageView(RenderingContext context, in ImageView vkObject)
            : base(in vkObject)
        {
            _context = context;
        }

        public unsafe static VkImageView Create(RenderingContext context, in ImageViewCreateInfo ci)
        {
            RenderingContext.Vk.CreateImageView(context.Device, in ci, in RenderingContext.CustomAllocator, out var imageView)
               .VkAssertResult("Failed to create image view!");
            return new VkImageView(context, imageView);
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            RenderingContext.Vk.DestroyImageView(_context.Device, _vkObject, in RenderingContext.CustomAllocator);
            _disposed = true;
        }
    }
}
