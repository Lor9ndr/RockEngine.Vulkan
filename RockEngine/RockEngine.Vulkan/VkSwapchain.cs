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

        private readonly SwapchainFrameData[] _frameData;
        private int _currentFrameIndex = 0;
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
        public int CurrentFrameIndex => _currentFrameIndex;
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
            swapchainApi.CreateSwapchain(context.Device, in createInfo, null, out var swapChain)
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
                return PresentModeKHR.MailboxKhr;
            if (modes.Contains(PresentModeKHR.ImmediateKhr))
                return PresentModeKHR.ImmediateKhr;
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
                var image = images[i];
                var ci = new ImageViewCreateInfo()
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = image,
                    ViewType = ImageViewType.Type2D,
                    Format = format,
                    Components = new ComponentMapping()
                    {
                        A = ComponentSwizzle.Identity,
                        B = ComponentSwizzle.Identity,
                        G = ComponentSwizzle.Identity,
                        R = ComponentSwizzle.Identity,
                    },
                    SubresourceRange = new ImageSubresourceRange()
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    }
                };
                swapChainImageViews[i] = VkImageView.Create(context, image, in ci);
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
        public unsafe Result AcquireNextImage()
        {
            var currentFrame = GetFrameData();

            // Optimized fence wait using queue operations instead of CPU stall
           currentFrame.FlushOperation?.Wait();

            var result = _khrSwapchain.AcquireNextImage(
                _context.Device,
                _vkObject,
                ulong.MaxValue,
                currentFrame.ImageAvailableSemaphore,
                default,
                ref _currentImageIndex);

            if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
            {
                RecreateSwapchain();
                return result;
            }

            return result.VkAssertResult("Failed to acquire swapchain image");
        }

    
        public SwapchainFrameData GetFrameData()
        {
            return _frameData[_currentFrameIndex];
        }

        public unsafe void Present(FlushOperation operation)
        {
            var currentFrame = _frameData[_currentFrameIndex];
            var swapchains =  _vkObject;
            var imageIndices = _currentImageIndex;
            var waitSemaphores = currentFrame.RenderFinishedSemaphore.VkObjectNative;

            var presentInfo = new PresentInfoKHR
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &waitSemaphores,
                SwapchainCount = 1,
                PSwapchains = &swapchains,
                PImageIndices = &imageIndices
            };

            var presentResult = _khrSwapchain.QueuePresent(_context.Device.PresentQueue, in presentInfo);

            if (presentResult == Result.ErrorOutOfDateKhr ||
                presentResult == Result.SuboptimalKhr)
            {
                RecreateSwapchain();
            }
            else
            {
                presentResult.VkAssertResult("Failed to present queue");
            }
            currentFrame.FlushOperation = operation;
            _currentFrameIndex = (_currentFrameIndex + 1) % _context.MaxFramesPerFlight;

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
            DisposeImageViews();

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

                _khrSwapchain.CreateSwapchain(_context.Device, in createInfo,null, out var swapChain)
                    .VkAssertResult("Failed to create swapchain");
                uint imagesCount = 0;
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, ref imagesCount, default);
                var images = new Span<Image>(new Image[imageCount]);
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, &imagesCount, images);
                _khrSwapchain.DestroySwapchain(_context.Device, oldSwapchain,null);
                _vkObject = swapChain;
                int i = 0;
                foreach (var item in images)
                {
                    _images[i].InternalChangeVkObject(item);
                    i++;
                }
                _extent = extent;

                // Recreate image views and framebuffers
                InitializeSwapchainResources();
                _currentFrameIndex = 0;
            }
        }

        private void DisposeImageViews()
        {
            foreach (var item in _swapChainImageViews)
            {
                item.Dispose();
            }
            _depthImage.Dispose();
            _depthImageView.Dispose();
        }

        private unsafe VkRenderPass CreateRenderPass()
        {
            var colorAttachment = new AttachmentDescription
            {
                Format = _format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.Clear,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr
            };

            var colorAttachmentReference = new AttachmentReference
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal
            };

            _depthFormat = FindDepthFormat();

            var depthAttachment = new AttachmentDescription
            {
                Format = _depthFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };

            var depthAttachmentReference = new AttachmentReference
            {
                Attachment = 1,
                Layout = ImageLayout.DepthStencilAttachmentOptimal
            };

            var description = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorAttachmentReference,
                PDepthStencilAttachment = &depthAttachmentReference,
            };

            var dependency = new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                SrcAccessMask = AccessFlags.None,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
            };

            return VkRenderPass.Create(_context, [description], [colorAttachment, depthAttachment], new[] { dependency });
        }

        public unsafe void CreateDepthResources()
        {
            using var commandPool = VkCommandPool.Create(_context, new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit,
                QueueFamilyIndex = _context.Device.QueueFamilyIndices.GraphicsFamily.Value
            });
            using var commandBuffer = commandPool.AllocateCommandBuffer();
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

            commandBuffer.BeginSingleTimeCommand();
            _depthImage.TransitionImageLayout(commandBuffer, ImageLayout.DepthStencilAttachmentOptimal, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.EarlyFragmentTestsBit,0, 1);
            commandBuffer.End();

            var nativeBuffer = commandBuffer.VkObjectNative;
            _context.Device.GraphicsQueue.Submit(new SubmitInfo(StructureType.SubmitInfo) { CommandBufferCount = 1, PCommandBuffers = &nativeBuffer }, fence);
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

        public class SwapchainFrameData :IDisposable
        {
            public VkSemaphore ImageAvailableSemaphore { get; }
            public VkSemaphore RenderFinishedSemaphore { get; }
            public VkFence InFlightFence { get; }
            public FlushOperation FlushOperation { get; internal set; }

            public SwapchainFrameData(VulkanContext context)
            {
                ImageAvailableSemaphore = VkSemaphore.Create(context);
                RenderFinishedSemaphore = VkSemaphore.Create(context);
                InFlightFence = VkFence.CreateSignaled(context);
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

            DisposeImageViews();

            foreach (var frameData in _frameData)
            {
                frameData.Dispose();
            }

            _khrSwapchain.DestroySwapchain(_context.Device, _vkObject,null);
            _vkObject = default;
            _disposed = true;
        }
    }
}
