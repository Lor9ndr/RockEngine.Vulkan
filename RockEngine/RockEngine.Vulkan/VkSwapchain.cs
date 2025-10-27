using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;


namespace RockEngine.Vulkan
{
    public class VkSwapchain : VkObject<SwapchainKHR>
    {
        private readonly KhrSwapchain _khrSwapchain;
        private readonly Format _format;
        private readonly VulkanContext _context;

        private VkImage[] _images;
        private Extent2D _extent;
        private readonly ISurfaceHandler _surface;
        private readonly VkImageView[] _swapChainImageViews;

        private SwapchainFrameData[] _frameData;
        private readonly VkSwapchain? _oldSwapchain;
        private VkImage _depthImage;
        private VkImageView _depthImageView;
        private Format _depthFormat;
        private uint _currentImageIndex;

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
            _frameData = new SwapchainFrameData[context.MaxFramesPerFlight];
            for (int i = 0; i < context.MaxFramesPerFlight; i++)
            {
                _frameData[i] = new SwapchainFrameData(context);
            }

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
                ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
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
            return new VkSwapchain(context, swapChain, swapchainApi, vkImages, surfaceFormat.Format, extent, surface);
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
                return capabilities.CurrentExtent;
            }
            else
            {
                return new Extent2D
                {
                    Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, width)),
                    Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, height))
                };
            }
        }

        private static PresentModeKHR ChoosePresentMode(PresentModeKHR[] modes)
        {
            if (modes.Contains(PresentModeKHR.MailboxKhr))
            {
                return PresentModeKHR.MailboxKhr;
            }

            if (modes.Contains(PresentModeKHR.ImmediateKhr))
            {
                return PresentModeKHR.ImmediateKhr;
            }

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
            return availableFormats[0];
        }

        private void InitializeSwapchainResources()
        {
            // Recreate image views
            FillImageViews(_context, _images, _format, _swapChainImageViews);

            // Recreate depth resources
            CreateDepthResources();
            // Notify listeners about the swapchain recreation
            OnSwapchainRecreate?.Invoke(this);

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
        public unsafe Result AcquireNextImage(uint frameIndex)
        {
            var currentFrame = _frameData[frameIndex];
            uint imageIndex = 0;

            var result = _khrSwapchain.AcquireNextImage(
                _context.Device,
                _vkObject,
                ulong.MaxValue,
                currentFrame.ImageAvailableSemaphore,
                default,
                ref imageIndex);

            _currentImageIndex = imageIndex;
            return result;
        }


        public SwapchainFrameData GetFrameData(uint frameIndex)
        {
            return _frameData[frameIndex];
        }

        public unsafe Result Present(uint frameIndex, FlushOperation flushOperation)
        {
            var currentFrame = _frameData[frameIndex];
            var swapchains = _vkObject;
            var imageIndices = _currentImageIndex;
            var waitSemaphores = currentFrame.RenderFinishedSemaphore.VkObjectNative;
            currentFrame.FlushOperation = flushOperation;

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


        public void RecreateSwapchain()
        {
            // Wait until the window is no longer minimized
            while (_surface.Window.WindowState == Silk.NET.Windowing.WindowState.Minimized)
            {
                _surface.Window.DoEvents();
            }

            // Wait for the device to finish any ongoing operations
            _context.Device.GraphicsQueue.WaitIdle();
            _context.Device.PresentQueue.WaitIdle();

            // Dispose of old framebuffers and image views
            DisposeImagesAndViews();

            // Recreate the swapchain
            var oldSwapchain = _vkObject;

            unsafe
            {
                var swapChainSupport = VkHelper.QuerySwapChainSupport(_context.Device.PhysicalDevice, _surface);
                var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
                var presentMode = ChoosePresentMode(swapChainSupport.PresentModes);
                var extent = ChooseSwapExtent(swapChainSupport.Capabilities, (uint)Surface.Size.X, (uint)Surface.Size.Y);

                uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
                if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
                {
                    imageCount = swapChainSupport.Capabilities.MaxImageCount;
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
                    ImageUsage = ImageUsageFlags.ColorAttachmentBit,
                    PreTransform = swapChainSupport.Capabilities.CurrentTransform,
                    CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                    PresentMode = presentMode,
                    Clipped = true,
                    OldSwapchain = _vkObject
                };
                var queueFamilyIndices = stackalloc uint[] { _context.Device.QueueFamilyIndices.GraphicsFamily.Value, _context.Device.QueueFamilyIndices.PresentFamily.Value };

                if (_context.Device.QueueFamilyIndices.GraphicsFamily != _context.Device.QueueFamilyIndices.PresentFamily)
                {
                    createInfo.ImageSharingMode = SharingMode.Concurrent;
                    createInfo.QueueFamilyIndexCount = 2;
                    createInfo.PQueueFamilyIndices = queueFamilyIndices;
                }
                else
                {
                    createInfo.ImageSharingMode = SharingMode.Exclusive;
                }

                _khrSwapchain.CreateSwapchain(_context.Device, in createInfo, in VulkanContext.CustomAllocator<VkSwapchain>(), out var swapChain)
                    .VkAssertResult("Failed to create swapchain");
                uint imagesCount = 0;
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, ref imagesCount, default);
                var images = new Span<Image>(new Image[imageCount]);
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, &imagesCount, images);
                _khrSwapchain.DestroySwapchain(_context.Device, oldSwapchain, in VulkanContext.CustomAllocator<VkSwapchain>());

                _vkObject = swapChain;
                for (int i = 0; i < images.Length; i++)
                {
                    _images[i].InternalChangeVkObject(in images[i]);
                }
                _extent = extent;
                if (imagesCount != _images.Length)
                {
                    // Dispose old frame data
                    foreach (var frame in _frameData)
                    {
                        frame.Dispose();
                    }
                    // Recreate frame data array
                    _frameData = new SwapchainFrameData[imagesCount];
                    for (int i = 0; i < _frameData.Length; i++)
                    {
                        _frameData[i] = new SwapchainFrameData(_context);
                    }
                    _context.MaxFramesPerFlight = (int)imagesCount;
                }

               
                // Recreate image views and framebuffers
                InitializeSwapchainResources();
            }

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

            _depthImage.TransitionImageLayout(batch.CommandBuffer, ImageLayout.DepthStencilAttachmentOptimal, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.EarlyFragmentTestsBit, 0, 1);
            _context.GraphicsSubmitContext.FlushSingle(batch, fence).Wait();

            fence.Wait();


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

        public class SwapchainFrameData : IDisposable
        {
            public VkSemaphore ImageAvailableSemaphore { get; }
            public VkSemaphore RenderFinishedSemaphore { get; }
            public VkFence InFlightFence { get; }
            public FlushOperation FlushOperation { get; internal set; }

            public SwapchainFrameData(VulkanContext context)
            {
                ImageAvailableSemaphore = VkSemaphore.Create(context);
                ImageAvailableSemaphore.LabelObject(nameof(ImageAvailableSemaphore));

                RenderFinishedSemaphore = VkSemaphore.Create(context);
                RenderFinishedSemaphore.LabelObject(nameof(RenderFinishedSemaphore));

                InFlightFence = VkFence.CreateSignaled(context);
                InFlightFence.LabelObject(nameof(InFlightFence));
            }

            public void Dispose()
            {
                ImageAvailableSemaphore.Dispose();
                RenderFinishedSemaphore.Dispose();
                InFlightFence.Dispose();
            }
        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.SwapchainKhr, name);

        protected override unsafe void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            DisposeImagesAndViews();

            foreach (var frameData in _frameData)
            {
                frameData.Dispose();
            }

            _khrSwapchain.DestroySwapchain(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkSwapchain>());
            _vkObject = default;
            _disposed = true;
        }
    }
}