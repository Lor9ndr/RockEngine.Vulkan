
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkImageView : VkObject<ImageView>
    {
        private readonly VulkanContext _context;
        private readonly VkImage _image;
        public VkImage Image => _image;

        private VkImageView(VulkanContext context, VkImage image, in ImageView vkObject)
            : base(in vkObject)
        {
            _context = context;
            _image = image;
        }

        public static unsafe VkImageView Create(VulkanContext context, VkImage image, in ImageViewCreateInfo ci)
        {
            VulkanContext.Vk.CreateImageView(context.Device, in ci, in VulkanContext.CustomAllocator<VkImageView>(), out var imageView)
               .VkAssertResult("Failed to create image view!");
            return new VkImageView(context, image, imageView);
        }
        public static VkImageView Create(
            VulkanContext context,
            VkImage image,
            Format format,
            ImageAspectFlags aspectFlags,
            ImageViewType viewType = ImageViewType.Type2D,
            uint mipLevels = 1,
            uint arrayLayers = 1,
            uint baseMipLevel = 0)
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
                    BaseMipLevel = baseMipLevel,
                    LevelCount = mipLevels,
                    BaseArrayLayer = 0,
                    LayerCount = arrayLayers
                }
            };

            return Create(context, image, createInfo);
        }


        protected override unsafe void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            VulkanContext.Vk.DestroyImageView(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkImageView>());
            _disposed = true;
        }
    }
}
