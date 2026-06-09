using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkImageView : VkObject<ImageView>, IResourceTrackable
    {
        private readonly VulkanContext _context;
        private readonly VkImage _image;
        private ImageViewCreateInfo _createInfo;
        private readonly ImageObserver _imageObserver;
        private readonly IDisposable _imageSubscription; // подписка на изменения изображения

        private readonly ResourceTracker _tracker = new ResourceTracker();
        public ulong ID => _tracker.ID;
        public IDisposable Subscribe(IResourceObserver observer) => _tracker.Subscribe(observer);

        public Format Format => _createInfo.Format;
        public ImageAspectFlags AspectFlags => _createInfo.SubresourceRange.AspectMask;
        public ImageViewType ViewType => _createInfo.ViewType;
        public uint BaseMipLevel => _createInfo.SubresourceRange.BaseMipLevel;
        public uint BaseArrayLayer => _createInfo.SubresourceRange.BaseArrayLayer;
        public uint LevelCount => _createInfo.SubresourceRange.LevelCount;
        public uint LayerCount => _createInfo.SubresourceRange.LayerCount;

        public VkImage Image => _image;

        private VkImageView(VulkanContext context, VkImage image, in ImageView vkObject, in ImageViewCreateInfo ci)
            : base(in vkObject)
        {
            _context = context;
            _image = image;
            _createInfo = ci;
            _imageObserver = new ImageObserver(this);
            _imageSubscription = _image.Subscribe(_imageObserver);
        }

        // Внутренний наблюдатель за изменениями VkImage
        private sealed class ImageObserver(VkImageView imageView) : IResourceObserver
        {
            public void OnResourceChanged(ulong resourceId, ResourceChangeType changeType)
            {
                if (changeType == ResourceChangeType.Resized)
                {
                    imageView.Recreate();
                }
            }
        }

        public static VkImageView Create(VulkanContext context, VkImage image, in ImageViewCreateInfo ci)
        {
            VulkanContext.Vk.CreateImageView(context.Device, in ci, in VulkanContext.CustomAllocator<VkImageView>(), out var imageView)
               .VkAssertResult("Failed to create image view!");
            return new VkImageView(context, image, imageView, in ci);
        }

        public static VkImageView Create(
            VulkanContext context,
            VkImage image,
            Format format,
            ImageAspectFlags aspectFlags,
            ImageViewType type = ImageViewType.Type2D,
            uint baseMipLevel = 0,
            uint levelCount = 1,
            uint baseArrayLayer = 0,
            uint arrayLayers = 1)
        {
            var createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = type,
                Format = format,
                Components = new ComponentMapping(),
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspectFlags,
                    BaseMipLevel = baseMipLevel,
                    LevelCount = levelCount,
                    BaseArrayLayer = baseArrayLayer,
                    LayerCount = arrayLayers
                }
            };
            return Create(context, image, createInfo);
        }

        private void Recreate()
        {
            // Destroy existing view
            if (_vkObject.Handle != 0)
            {
                VulkanContext.Vk.DestroyImageView(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkImageView>());
            }
            _createInfo.Image = _image;

            VulkanContext.Vk.CreateImageView(_context.Device, in _createInfo, in VulkanContext.CustomAllocator<VkImageView>(), out var imageView);
            _vkObject = imageView;

            // Уведомляем подписчиков об изменении вью
            _tracker.NotifyObservers(ResourceChangeType.DataUpdated);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _imageSubscription?.Dispose(); // отписываемся от изображения
                _tracker.NotifyObservers(ResourceChangeType.Disposed);
                _tracker.Clear();
            }

            _image.RemoveViewFromCache(this);
            VulkanContext.Vk.DestroyImageView(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkImageView>());
            _disposed = true;
        }

        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.ImageView, name);

        public void Update()
        {
            _tracker.NotifyObservers(ResourceChangeType.DataUpdated);
        }
    }
}