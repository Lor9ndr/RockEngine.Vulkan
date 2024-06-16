using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;
using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.VkObjects
{
    public class SwapchainWrapper : VkObject<SwapchainKHR>
    {
        private readonly KhrSwapchain _khrSwapchain;

        private readonly Silk.NET.Vulkan.Image[] _images;
        private readonly Format _format;
        private SwapchainKHR _swapchain;
        private readonly Extent2D _extent;
        private ImageView[] _swapChainImageViews;
        private readonly VulkanContext _context;

        public SwapchainKHR Swapchain => _swapchain;
        public KhrSwapchain SwapchainApi => _khrSwapchain;
        public Silk.NET.Vulkan.Image[] Images => _images;
        public Format Format => _format;
        public Extent2D Extent => _extent;

        public ImageView[] SwapChainImageViews => _swapChainImageViews;

        public SwapchainWrapper(VulkanContext context, SwapchainKHR swapchain, KhrSwapchain khrSwapchainApi, Silk.NET.Vulkan.Image[] images, Format format, Extent2D extent)
            :base(swapchain)
        {
            _swapChainImageViews = new ImageView[images.Length];
            _context = context;
            _swapchain = swapchain;
            _khrSwapchain = khrSwapchainApi;
            _images = images;
            _format = format;
            _extent = extent;
            CreateImageViews();
        }

        public unsafe static SwapchainWrapper Create(VulkanContext context, ISurfaceHandler surface, uint width, uint height)
        {
            // Assume SwapChainSupportDetails, ChooseSwapSurfaceFormat, ChooseSwapPresentMode, and ChooseSwapExtent are implemented
            var swapChainSupport = VkHelper.QuerySwapChainSupport(context.Device.PhysicalDevice, context.Surface);
            var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
            var presentMode = ChooseSwapPresentMode(swapChainSupport.PresentModes);
            var extent = ChooseSwapExtent(swapChainSupport.Capabilities, width, height);

            uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
            if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
            {
                imageCount = swapChainSupport.Capabilities.MaxImageCount;
            }

            var createInfo = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = context.Surface.Surface,
                MinImageCount = imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit,
                PreTransform = swapChainSupport.Capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PresentMode = presentMode,
                Clipped = Vk.True,
                OldSwapchain = default
            };

            // Handle queue family indices
            if (context.Device.QueueFamilyIndices.GraphicsFamily != context.Device.QueueFamilyIndices.PresentFamily)
            {
                uint[] queueFamilyIndices = new uint[2] { context.Device.QueueFamilyIndices.GraphicsFamily.Value, context.Device.QueueFamilyIndices.PresentFamily.Value };
                fixed (uint* pQueueFamilyIndices = queueFamilyIndices)
                {
                    createInfo.ImageSharingMode = SharingMode.Concurrent;
                    createInfo.QueueFamilyIndexCount = 2;
                    createInfo.PQueueFamilyIndices = pQueueFamilyIndices;
                }
            }
            else
            {
                createInfo.ImageSharingMode = SharingMode.Exclusive;
            }

            var swapchainApi = new KhrSwapchain(context.Api.Context);
            var result = swapchainApi.CreateSwapchain(context.Device, in createInfo, null, out var swapChain);

            if (result != Result.Success)
            {
                throw new Exception("Failed to create swap chain");
            }

            uint countImages = 0;
            swapchainApi.GetSwapchainImages(context.Device, swapChain, &countImages, null);
            var images = new Silk.NET.Vulkan.Image[countImages];
            swapchainApi.GetSwapchainImages(context.Device, swapChain, &countImages, images);

            return new SwapchainWrapper(context, swapChain, swapchainApi, images, surfaceFormat.Format, extent);
        }

        private static Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities, uint width, uint height)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
            {
                return capabilities.CurrentExtent;
            }
            else
            {
                Extent2D actualExtent = new Extent2D
                {
                    Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, width)),
                    Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, height))
                };

                return actualExtent;
            }
        }

        private static PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] availablePresentModes)
        {
            foreach (var availablePresentMode in availablePresentModes)
            {
                if (availablePresentMode == PresentModeKHR.MailboxKhr)
                {
                    return availablePresentMode;
                }
            }

            // FIFO is always available as per Vulkan spec
            return PresentModeKHR.FifoKhr;
        }

        private static SurfaceFormatKHR ChooseSwapSurfaceFormat(SurfaceFormatKHR[] availableFormats)
        {
            foreach (var availableFormat in availableFormats)
            {
                if (availableFormat.Format == Format.B8G8R8A8Srgb && availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return availableFormat;
                }
            }

            // If the preferred format is not found, return the first format available
            return availableFormats[0];
        }

        private unsafe void CreateImageViews()
        {
            for (int i = 0; i < _images.Length; i++)
            {
                Silk.NET.Vulkan.Image image = _images[i];
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

                _context.Api.CreateImageView(_context.Device, ref createInfo, null, out var imageView)
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
                    _khrSwapchain.DestroySwapchain(_context.Device, _swapchain, null);
                    _swapchain = default;
                    if (_swapChainImageViews.Length != 0)
                    {
                        foreach (var imageView in _swapChainImageViews)
                        {
                            _context.Api.DestroyImageView(_context.Device, imageView, null);
                        }
                    }
                }

                _disposed = true;
            }
        }

        ~SwapchainWrapper()
        {
            Dispose(disposing: false);
        }
    }
}
