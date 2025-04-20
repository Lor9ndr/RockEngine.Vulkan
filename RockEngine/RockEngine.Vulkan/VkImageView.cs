
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkImageView : VkObject<ImageView>
    {
        private readonly VulkanContext _context;
        private VkImage _image;
        private ImageViewCreateInfo _createInfo;

        public Format Format => _createInfo.Format;
        public ImageAspectFlags AspectFlags => _createInfo.SubresourceRange.AspectMask;
        public uint MipLevels => _createInfo.SubresourceRange.BaseMipLevel;

        public VkImage Image => _image;

        public event Action? WasUpdated;

        private VkImageView(VulkanContext context, VkImage image, in ImageView vkObject, in ImageViewCreateInfo ci)
            : base(in vkObject)
        {
            _context = context;
            _image = image;
            _createInfo = ci;
            _image.OnImageResized += Recreate;
        }


        public static unsafe VkImageView Create(VulkanContext context, VkImage image, in ImageViewCreateInfo ci)
        {
            VulkanContext.Vk.CreateImageView(context.Device, in ci, in VulkanContext.CustomAllocator<VkImageView>(), out var imageView)
               .VkAssertResult("Failed to create image view!");
            return new VkImageView(context, image, imageView,in ci);
        }

        private unsafe void Recreate(VkImage image)
        {
            // Destroy existing view
            if (_vkObject.Handle != 0)
            {
                VulkanContext.Vk.DestroyImageView(_context.Device, _vkObject, null);
            }
            _image = image;
            _createInfo.Image = _image;
           

            VulkanContext.Vk.CreateImageView(_context.Device, in _createInfo, null, out var imageView);
            _vkObject = imageView;

            WasUpdated?.Invoke();
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
