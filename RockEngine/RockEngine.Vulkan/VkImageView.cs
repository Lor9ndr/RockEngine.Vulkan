
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkImageView : VkObject<ImageView>
    {
        private readonly RenderingContext _context;
        private readonly VkImage _image;
        public VkImage Image => _image;

        private VkImageView(RenderingContext context, VkImage image, in ImageView vkObject)
            : base(in vkObject)
        {
            _context = context;
            _image = image;
        }

        public unsafe static VkImageView Create(RenderingContext context, VkImage image, in ImageViewCreateInfo ci)
        {
            RenderingContext.Vk.CreateImageView(context.Device, in ci, in RenderingContext.CustomAllocator<VkImageView>(), out var imageView)
               .VkAssertResult("Failed to create image view!");
            return new VkImageView(context, image, imageView);
        }
        public static VkImageView Create(
            RenderingContext context,
            VkImage image,
            Format format,
            ImageAspectFlags aspectFlags,
            ImageViewType viewType = ImageViewType.Type2D,
            uint mipLevels = 1,
            uint arrayLayers = 1)
        {
            var createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image.VkObjectNative,
                ViewType = viewType,
                Format = format,
                Components = new ComponentMapping(),
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspectFlags,
                    BaseMipLevel = 0,
                    LevelCount = mipLevels,
                    BaseArrayLayer = 0,
                    LayerCount = arrayLayers
                }
            };

            return Create(context, image, createInfo);
        }


        protected unsafe override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            RenderingContext.Vk.DestroyImageView(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkImageView>());
            _disposed = true;
        }
    }
}
