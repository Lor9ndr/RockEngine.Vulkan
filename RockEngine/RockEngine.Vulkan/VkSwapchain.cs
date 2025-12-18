using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

using System.Numerics;


namespace RockEngine.Vulkan
{
    public class VkSwapchain : VkObject<SwapchainKHR>
    {
        private readonly KhrSwapchain _khrSwapchain;
        private readonly Format _format;
        private readonly VulkanContext _context;

        private readonly VkImage[] _images;
        private Extent2D _extent;
        private readonly ISurfaceHandler _surface;
        private readonly VkImageView[] _swapChainImageViews;

        private readonly VkSwapchain? _oldSwapchain;
        private VkImage _depthImage;
        private VkImageView _depthImageView;
        private readonly Format _depthFormat;
        private uint _currentImageIndex = 0;

        public KhrSwapchain SwapchainApi => _khrSwapchain;
        public VkImage[] VkImages => _images;
        public Format Format => _format;
        public Extent2D Extent => _extent;
        public VkImageView[] SwapChainImageViews => _swapChainImageViews;
        public VkSwapchain? OldSwapchain => _oldSwapchain;
        public int SwapChainImagesCount => _images.Length;
        public Format DepthFormat => _depthFormat;
        public ISurfaceHandler Surface => _surface;
        public uint CurrentImageIndex => _currentImageIndex;

        public VkImageView DepthImageView { get => _depthImageView; private set => _depthImageView = value; }

        public event Action<VkSwapchain>? OnSwapchainRecreate;

        public VkSwapchain(VulkanContext context, SwapchainKHR swapchain, KhrSwapchain khrSwapchainApi, VkImage[] images, Format format, Extent2D extent, ISurfaceHandler surface)
            : base(swapchain)
        {
            context.MaxFramesPerFlight = images.Length;

            _swapChainImageViews = new VkImageView[images.Length];

            _context = context;
            _khrSwapchain = khrSwapchainApi;
            _images = images;
            _format = format;
            _extent = extent;
            _surface = surface;
            _depthFormat = FindDepthFormat();

            InitializeSwapchainResources();
        }
        private static uint CalculateImageCount(SurfaceCapabilitiesKHR capabilities)
        {
            uint count = capabilities.MinImageCount + 1;
            return capabilities.MaxImageCount > 0
                ? Math.Min(count, capabilities.MaxImageCount)
                : count;
        }

        public static unsafe VkSwapchain Create(VulkanContext context, ISurfaceHandler surface)
        {
            var swapChainSupport = VkHelper.QuerySwapChainSupport(context.Device.PhysicalDevice, surface);
            var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
            var presentMode = ChoosePresentMode(swapChainSupport.PresentModes);
            var extent = ChooseSwapExtent(swapChainSupport.Capabilities, (uint)surface.Size.X, (uint)surface.Size.Y);

            uint imageCount = CalculateImageCount(swapChainSupport.Capabilities);
            var createInfo = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = surface.Surface,
                MinImageCount = imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit /*| ImageUsageFlags.TransferSrcBit*/,
                PreTransform = swapChainSupport.Capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PresentMode = presentMode,
                Clipped = true,
                OldSwapchain = default
            };

            // Get queue family indices
            var indices = context.Device.QueueFamilyIndices;
            uint graphicsFamily = indices.GraphicsFamily!.Value;
            uint presentFamily = indices.PresentFamily!.Value;

            var queueFamilyIndices = stackalloc uint[] { context.Device.QueueFamilyIndices.GraphicsFamily.Value, context.Device.QueueFamilyIndices.PresentFamily.Value };
            if (context.Device.QueueFamilyIndices.GraphicsFamily != context.Device.QueueFamilyIndices.PresentFamily)
            {
                createInfo.ImageSharingMode = SharingMode.Concurrent;
                createInfo.QueueFamilyIndexCount = 2;
                createInfo.PQueueFamilyIndices = queueFamilyIndices;
            }
            else
            {
                createInfo.ImageSharingMode = SharingMode.Exclusive;
            }

            var swapchainApi = new KhrSwapchain(VulkanContext.Vk.Context);
            swapchainApi.CreateSwapchain(context.Device, in createInfo, in VulkanContext.CustomAllocator<VkSwapchain>(), out var swapChain)
                .VkAssertResult("Failed to create swapchain");

            uint countImages = 0;
            swapchainApi.GetSwapchainImages(context.Device, swapChain, &countImages, null);
            var images = new Image[countImages];
            swapchainApi.GetSwapchainImages(context.Device, swapChain, &countImages, images);
            var ci = new ImageCreateInfo()
            {
                SType = StructureType.ImageCreateInfo,
                Format = surfaceFormat.Format,
                InitialLayout = ImageLayout.Undefined,
                MipLevels = 1,
                ArrayLayers = 1
            };
            var vkImages = images.Select(s => new VkImage(context, s, null, ci, ImageAspectFlags.ColorBit)).ToArray();
            for (int i = 0; i < vkImages.Length; i++)
            {
                VkImage? vkImage = vkImages[i];
                vkImage.LabelObject($"Swapchain Image {i}, of swapchain :{swapChain.Handle}");
            }

            var swapchain = new VkSwapchain(context, swapChain, swapchainApi, vkImages, surfaceFormat.Format, extent, surface);

            // Ensure initial layout transition
            swapchain.TransitionSwapchainImagesToPresentLayout();

            return swapchain;
        }


        private static unsafe void SetImageSharingMode(VulkanContext context, ref SwapchainCreateInfoKHR createInfo)
        {
            if (context.Device.QueueFamilyIndices.GraphicsFamily != context.Device.QueueFamilyIndices.PresentFamily)
            {
                var queueFamilyIndices = stackalloc uint[] { context.Device.QueueFamilyIndices.GraphicsFamily.Value, context.Device.QueueFamilyIndices.PresentFamily.Value };
                createInfo.ImageSharingMode = SharingMode.Concurrent;
                createInfo.QueueFamilyIndexCount = 2;
                createInfo.PQueueFamilyIndices = queueFamilyIndices;
            }
            else
            {
                createInfo.ImageSharingMode = SharingMode.Exclusive;
            }
        }

        private static Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities, uint width, uint height)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
            {
                // If current extent is valid, use it
                return capabilities.CurrentExtent;
            }
            else
            {
                // For viewports that might be zero-sized initially, provide a fallback
                uint actualWidth = Math.Max(width, 1);
                uint actualHeight = Math.Max(height, 1);

                uint clampedWidth = Math.Max(capabilities.MinImageExtent.Width,
                                           Math.Min(capabilities.MaxImageExtent.Width, actualWidth));
                uint clampedHeight = Math.Max(capabilities.MinImageExtent.Height,
                                            Math.Min(capabilities.MaxImageExtent.Height, actualHeight));

                return new Extent2D
                {
                    Width = clampedWidth,
                    Height = clampedHeight
                };
            }
        }

        private static PresentModeKHR ChoosePresentMode(PresentModeKHR[] modes)
        {
            if (modes.Contains(PresentModeKHR.FifoKhr))
            {
                return PresentModeKHR.FifoKhr;
            }

            if (modes.Contains(PresentModeKHR.MailboxKhr))
            {
                return PresentModeKHR.MailboxKhr;
            }

            if (modes.Contains(PresentModeKHR.ImmediateKhr))
            {
                return PresentModeKHR.ImmediateKhr;
            }
          

            return modes[0];
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
            return availableFormats[0];
        }

        private void InitializeSwapchainResources()
        {
            // Recreate image views
            FillImageViews(_context, _images, _format, _swapChainImageViews);

            // Recreate depth resources
            CreateDepthResources();
           /* if (_imageAvailableSemaphores != null)
            {
                foreach (var semaphore in _imageAvailableSemaphores)
                {
                    semaphore?.Dispose();
                }
            }

            if (_renderCompleteSemaphores != null)
            {
                foreach (var semaphore in _renderCompleteSemaphores)
                {
                    semaphore?.Dispose();
                }
            }*/

            // Reinitialize with current frame count
           
            _currentImageIndex = 0;
            // Notify listeners about the swapchain recreation

        }

        private void FillImageViews(VulkanContext context, VkImage[] images, Format format, VkImageView[] swapChainImageViews)
        {
            for (int i = 0; i < images.Length; i++)
            {
                swapChainImageViews[i] = images[i].GetOrCreateView(ImageAspectFlags.ColorBit);
            }
        }

        private unsafe void FillFramebuffers(VulkanContext context, VkImageView[][] framebufferAttachments, VkRenderPass renderPass, Extent2D extent, Image[] images, VkFrameBuffer[] swapchainFramebuffers)
        {
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                var attachment = framebufferAttachments[i];
                fixed (ImageView* pImageViews = attachment.Select(s => s.VkObjectNative).ToArray())
                {
                    FramebufferCreateInfo ci = new FramebufferCreateInfo()
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = renderPass,
                        AttachmentCount = (uint)attachment.Length,
                        PAttachments = pImageViews,
                        Width = extent.Width,
                        Height = extent.Height,
                        Layers = 1
                    };
                    swapchainFramebuffers[i] = VkFrameBuffer.Create(context, in ci, attachment);
                }
            }
        }
        public Result AcquireNextImage(VkSemaphore imageAvailable, out uint imageIndex)
        {
            var semaphore = imageAvailable.VkObjectNative;

            var result = _khrSwapchain.AcquireNextImage(
                _context.Device,
                _vkObject,
                ulong.MaxValue,
                semaphore,
                default,
                ref _currentImageIndex);
            imageIndex = _currentImageIndex;
            return result;
        }


        public unsafe Result Present(FrameData frame)
        {
            var currentFrame = frame;
            var swapchains = _vkObject;
            var imageIndices = _currentImageIndex;
            var waitSemaphores = currentFrame.RenderFinished.VkObjectNative;

            var presentInfo = new PresentInfoKHR
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &waitSemaphores,
                SwapchainCount = 1,
                PSwapchains = &swapchains,
                PImageIndices = &imageIndices,
            };

            var result = _khrSwapchain.QueuePresent(_context.Device.PresentQueue, in presentInfo);
            return result.VkAssertResult("Failed to present queue", Result.ErrorOutOfDateKhr, Result.SuboptimalKhr, Result.ErrorSurfaceLostKhr);
        }
        public Result Present(VkSemaphore renderComplete)
        {
            var swapchainHandle = VkObjectNative;
            var imageIndex = CurrentImageIndex;
            var waitSemaphore = renderComplete.VkObjectNative;

            unsafe
            {
                var presentInfo = new PresentInfoKHR
                {
                    SType = StructureType.PresentInfoKhr,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = &waitSemaphore,
                    SwapchainCount = 1,
                    PSwapchains = &swapchainHandle,
                    PImageIndices = &imageIndex,
                    PResults = null
                };

                return _khrSwapchain.QueuePresent(_context.Device.PresentQueue, in presentInfo);
            }
        }

        public void RecreateSwapchain()
        {
            // Wait for device idle
            _context.Device.GraphicsQueue.WaitIdle();
            _context.Device.PresentQueue.WaitIdle();

            // Check if window is valid and has non-zero size
            if (_surface.Window.WindowState == Silk.NET.Windowing.WindowState.Minimized ||
                _surface.Window.Size.X <= 0 || _surface.Window.Size.Y <= 0)
            {
                // Don't recreate swapchain for minimized or zero-sized windows
                Console.WriteLine($"Window is minimized or has zero size: {_surface.Window.Size}");
                return;
            }

            // Dispose of old resources
            DisposeImagesAndViews();

            var oldSwapchain = _vkObject;

            unsafe
            {
                // Query swapchain support
                var swapChainSupport = VkHelper.QuerySwapChainSupport(_context.Device.PhysicalDevice, _surface);

                // Ensure surface is still valid
                if (swapChainSupport.Capabilities.MaxImageExtent.Width == 0 ||
                    swapChainSupport.Capabilities.MaxImageExtent.Height == 0)
                {
                    Console.WriteLine("Surface has invalid capabilities, cannot recreate swapchain");
                    return;
                }

                var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
                var presentMode = ChoosePresentMode(swapChainSupport.PresentModes);

                // Get current window size, ensure it's at least 1x1
                uint windowWidth = Math.Max((uint)_surface.Window.Size.X, 1);
                uint windowHeight = Math.Max((uint)_surface.Window.Size.Y, 1);

                var extent = ChooseSwapExtent(swapChainSupport.Capabilities, windowWidth, windowHeight);

                // Validate extent
                if (extent.Width == 0 || extent.Height == 0)
                {
                    Console.WriteLine($"Invalid swapchain extent: {extent.Width}x{extent.Height}");
                    return;
                }

                uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
                if (swapChainSupport.Capabilities.MaxImageCount > 0 &&
                    imageCount > swapChainSupport.Capabilities.MaxImageCount)
                {
                    imageCount = swapChainSupport.Capabilities.MaxImageCount;
                }

                // Handle preTransform - ensure it's valid
                var preTransform = swapChainSupport.Capabilities.CurrentTransform;
                if (preTransform == 0)
                {
                    // Use Identity transform as fallback
                    preTransform = SurfaceTransformFlagsKHR.IdentityBitKhr;
                    Console.WriteLine($"Using Identity transform as fallback for swapchain");
                }

                // Validate preTransform is supported
                var supportedTransforms = swapChainSupport.Capabilities.SupportedTransforms;
                if ((supportedTransforms & preTransform) == 0)
                {
                    // If requested transform not supported, use a supported one
                    if ((supportedTransforms & SurfaceTransformFlagsKHR.IdentityBitKhr) != 0)
                    {
                        preTransform = SurfaceTransformFlagsKHR.IdentityBitKhr;
                    }
                    else
                    {
                        // Use the first supported transform
                        preTransform = (SurfaceTransformFlagsKHR)BitOperations.TrailingZeroCount((uint)supportedTransforms);
                    }
                    Console.WriteLine($"Adjusted preTransform to: {preTransform}");
                }

                var createInfo = new SwapchainCreateInfoKHR
                {
                    SType = StructureType.SwapchainCreateInfoKhr,
                    Surface = _surface.Surface,
                    MinImageCount = imageCount,
                    ImageFormat = surfaceFormat.Format,
                    ImageColorSpace = surfaceFormat.ColorSpace,
                    ImageExtent = extent,
                    ImageArrayLayers = 1,
                    ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
                    PreTransform = preTransform,
                    CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                    PresentMode = presentMode,
                    Clipped = true,
                    OldSwapchain = _vkObject
                };

                // Handle queue sharing mode
                if (_context.Device.QueueFamilyIndices.GraphicsFamily != _context.Device.QueueFamilyIndices.PresentFamily)
                {
                    var queueFamilyIndices = stackalloc uint[]
                    {
                        _context.Device.QueueFamilyIndices.GraphicsFamily.Value,
                        _context.Device.QueueFamilyIndices.PresentFamily.Value
                    };
                    createInfo.ImageSharingMode = SharingMode.Concurrent;
                    createInfo.QueueFamilyIndexCount = 2;
                    createInfo.PQueueFamilyIndices = queueFamilyIndices;
                }
                else
                {
                    createInfo.ImageSharingMode = SharingMode.Exclusive;
                }

                // Create the new swapchain
                var result = _khrSwapchain.CreateSwapchain(_context.Device, in createInfo,
                    in VulkanContext.CustomAllocator<VkSwapchain>(), out var swapChain);

                if (result != Result.Success)
                {
                    Console.WriteLine($"Failed to create swapchain: {result}");

                    if (result == Result.ErrorSurfaceLostKhr || result == Result.ErrorOutOfDateKhr)
                    {
                        Console.WriteLine("Attempting to recreate surface...");
                        return;
                    }

                    throw new VulkanException(result, $"Failed to create swapchain: {result}");
                }

                // Clean up old swapchain
                if (oldSwapchain.Handle != 0)
                {
                    _khrSwapchain.DestroySwapchain(_context.Device, oldSwapchain,
                        in VulkanContext.CustomAllocator<VkSwapchain>());
                }

                // Get swapchain images
                uint imagesCount = 0;
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, ref imagesCount, default);

                if (imagesCount == 0)
                {
                    Console.WriteLine("No images in swapchain!");
                    return;
                }

                var images = new Span<Image>(new Image[imagesCount]);
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, &imagesCount, images);

                _vkObject = swapChain;
                _extent = extent;

                // Update images
                for (int i = 0; i < Math.Min(images.Length, _images.Length); i++)
                {
                    _images[i].InternalChangeVkObject(in images[i]);
                }

                // Handle image count mismatch
                if (imagesCount != _images.Length)
                {
                    Console.WriteLine($"Image count changed: {_images.Length} -> {imagesCount}");
                    _context.MaxFramesPerFlight = (int)imagesCount;
                }

                // Recreate image views and depth resources
                InitializeSwapchainResources();

                TransitionSwapchainImagesToPresentLayout();
                OnSwapchainRecreate?.Invoke(this);

            }
        }

        private void TransitionSwapchainImagesToPresentLayout()
        {
            var batch = _context.GraphicsSubmitContext.CreateBatch();

            // Transition all swapchain images to PRESENT_SRC_KHR layout
            for (int i = 0; i < _images.Length; i++)
            {
                var image = _images[i];
                var barrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    OldLayout = ImageLayout.Undefined,
                    NewLayout = ImageLayout.PresentSrcKhr,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Image = image.VkObjectNative,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    SrcAccessMask = AccessFlags.None,
                    DstAccessMask = AccessFlags.ColorAttachmentWriteBit
                };

                batch.PipelineBarrier(
                    PipelineStageFlags.TopOfPipeBit,
                    PipelineStageFlags.ColorAttachmentOutputBit,
                    new Span<ImageMemoryBarrier>(ref barrier)
                );
            }

           using var fence = VkFence.CreateNotSignaled(_context);
            _context.GraphicsSubmitContext.FlushSingle(batch, fence).Wait();
        }

        private void DisposeImagesAndViews()
        {
            foreach (var item in _swapChainImageViews)
            {
                item.Dispose();
            }
            _depthImage.Dispose();
            _depthImageView.Dispose();
        }

        public unsafe void CreateDepthResources()
        {
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            
            using var fence = VkFence.CreateNotSignaled(_context);

            var imageCi = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                Extent = new Extent3D(_extent.Width, _extent.Height, 1),
                ImageType = ImageType.Type2D,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = _depthFormat,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.DepthStencilAttachmentBit,
                SharingMode = SharingMode.Exclusive,
                Samples = SampleCountFlags.Count1Bit,
                InitialLayout = ImageLayout.Undefined

            };
            var aspectMask = _depthFormat.HasStencilComponent()
           ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
           : ImageAspectFlags.DepthBit;
            _depthImage = VkImage.Create(_context, in imageCi, MemoryPropertyFlags.DeviceLocalBit, aspectMask);
            _depthImage.LabelObject("SwapchainDepthImage");

            _depthImage.TransitionImageLayout(batch, ImageLayout.DepthStencilAttachmentOptimal, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.EarlyFragmentTestsBit, 0, 1);
            _context.GraphicsSubmitContext.FlushSingle(batch, fence).Wait();


            var imageViewCi = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _depthImage,
                Format = _depthFormat,
                ViewType = ImageViewType.Type2D,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspectMask,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };
            _depthImageView = _depthImage.GetOrCreateView(aspectMask);

        }

        private Format FindDepthFormat() => FindSupportedFormat(
         [Format.D24UnormS8Uint, Format.D32Sfloat, Format.D32SfloatS8Uint], // Prefer D24S8 first
         ImageTiling.Optimal,
         FormatFeatureFlags.DepthStencilAttachmentBit
     );

        private Format FindSupportedFormat(Format[] candidates, ImageTiling tiling, FormatFeatureFlags features)
        {
            foreach (var format in candidates)
            {
                var properties = _context.Device.PhysicalDevice.GetFormatProperties(format);

                switch (tiling)
                {
                    case ImageTiling.Linear when (properties.LinearTilingFeatures & features) == features:
                        return format;
                    case ImageTiling.Optimal when (properties.OptimalTilingFeatures & features) == features:
                        return format;
                }
            }
            throw new InvalidOperationException("Failed to find supported format.");
        }

       
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.SwapchainKhr, name);

        protected override unsafe void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            DisposeImagesAndViews();


            _khrSwapchain.DestroySwapchain(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkSwapchain>());
            _vkObject = default;
            _disposed = true;
        }
    }
    public sealed class FrameData : IDisposable
    {
        public VkFence InFlightFence;
        public VkSemaphore ImageAvailable;
        public VkSemaphore RenderFinished;
        public UploadBatch CurrentBatch;
        public List<IDisposable> Resources = new();

        public void Reset()
        {
            //Fence will be awaited in the flush operation


            foreach (var resource in Resources)
                resource.Dispose();

            InFlightFence?.Reset();
            Resources.Clear();
        }

        public void Dispose()
        {
            InFlightFence?.Dispose();
            ImageAvailable?.Dispose();
            RenderFinished?.Dispose();

            foreach (var resource in Resources)
                resource.Dispose();
        }
    }
}