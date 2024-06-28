using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class ImageView : VkObject<Silk.NET.Vulkan.ImageView>
    {
        private readonly VulkanContext _context;

        private ImageView(VulkanContext context,in Silk.NET.Vulkan.ImageView vkObject)
            : base(in vkObject)
        {
            _context = context;
        }

        public unsafe static ImageView Create(VulkanContext context, in ImageViewCreateInfo ci)
        {
            context.Api.CreateImageView(context.Device, in ci, null, out var imageView)
               .ThrowCode("Failed to create image view!");
            return new ImageView(context, imageView);
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            _context.Api.DestroyImageView(_context.Device, _vkObject, null);
            _disposed = true;
        }
    }
}
