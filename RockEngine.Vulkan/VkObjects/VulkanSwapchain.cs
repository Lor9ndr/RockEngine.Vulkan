using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;
using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.VkObjects
{
    public class VulkanSwapchain : VkObject
    {
        private readonly KhrSwapchain _khrSwapchain;

        private readonly Image[] _images;
        private readonly Format _format;
        private SwapchainKHR _swapchain;
        private readonly Extent2D _extent;
        private ImageView[] _swapChainImageViews;
        private readonly VulkanContext _context;

        public SwapchainKHR Swapchain => _swapchain;
        public KhrSwapchain SwapchainApi => _khrSwapchain;
        public Image[] Images => _images;
        public Format Format => _format;
        public Extent2D Extent => _extent;

        public ImageView[] SwapChainImageViews => _swapChainImageViews;

        public VulkanSwapchain(VulkanContext context, SwapchainKHR swapchain, KhrSwapchain khrSwapchainApi, Image[] images, Format format, Extent2D extent)
        {
            _swapChainImageViews = new ImageView[images.Length];
            _context = context;
            _swapchain = swapchain;
            _khrSwapchain = khrSwapchainApi;
            _images = images;
            _format = format;
            _extent = extent;
        }

        public unsafe void CreateImageViews()
        {
            for (int i = 0; i < _images.Length; i++)
            {
                Image image = _images[i];
                var createInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = image,
                    ViewType = ImageViewType.Type2D,
                    Format = _format,
                    Components = new ComponentMapping
                    {
                        R = ComponentSwizzle.Identity,
                        G = ComponentSwizzle.Identity,
                        B = ComponentSwizzle.Identity,
                        A = ComponentSwizzle.Identity
                    },
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    }
                };

                _context.Api.CreateImageView(_context.Device.Device, ref createInfo, null, out var imageView)
                    .ThrowCode("Failed to create image views!");

                _swapChainImageViews[i] = imageView;
            }
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects) if any.
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
                if (_swapchain.Handle != 0)
                {
                    _khrSwapchain.DestroySwapchain(_context.Device.Device, _swapchain, null);
                    _swapchain = default;
                    if (_swapChainImageViews.Length != 0)
                    {
                        foreach (var imageView in _swapChainImageViews)
                        {
                            _context.Api.DestroyImageView(_context.Device.Device, imageView, null);
                        }
                    }
                }

                _disposed = true;
            }
        }

        ~VulkanSwapchain()
        {
            Dispose(disposing: false);
        }
    }
}
