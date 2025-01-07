using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;


namespace RockEngine.Vulkan
{
    public record VkSwapchain : VkObject<SwapchainKHR>
    {
        private readonly KhrSwapchain _khrSwapchain;
        private readonly Format _format;
        private readonly RenderingContext _context;

        private Image[] _images;
        private Extent2D _extent;
        private readonly ISurfaceHandler _surface;
        private readonly VkImageView[] _swapChainImageViews;

        private readonly FrameData[] _frameData;
        private int _currentFrame = 0;
        private VkSwapchain? _oldSwapchain;
        private VkImage _depthImage;
        private VkImageView _depthImageView;
        private Format _depthFormat;

        public KhrSwapchain SwapchainApi => _khrSwapchain;
        public Image[] Images => _images;
        public Format Format => _format;
        public Extent2D Extent => _extent;
        public VkImageView[] SwapChainImageViews => _swapChainImageViews;
        public VkSwapchain? OldSwapchain => _oldSwapchain;
        public int SwapChainImagesCount => _images.Length;
        public Format DepthFormat => _depthFormat;
        public ISurfaceHandler Surface => _surface;
        public int CurrentFrameIndex => _currentFrame;

        public VkImageView DepthImageView { get => _depthImageView; private set => _depthImageView = value; }

        public event Action<VkSwapchain>? OnSwapchainRecreate;

        public VkSwapchain(RenderingContext context, SwapchainKHR swapchain, KhrSwapchain khrSwapchainApi, Image[] images, Format format, Extent2D extent, ISurfaceHandler surface)
            : base(swapchain)
        {
            context.MaxFramesPerFlight = images.Length;

            _swapChainImageViews = new VkImageView[images.Length];
            _frameData = new FrameData[context.MaxFramesPerFlight];
            for (int i = 0; i < context.MaxFramesPerFlight; i++)
            {
                _frameData[i] = new FrameData(context);
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

        public unsafe static VkSwapchain Create(RenderingContext context, ISurfaceHandler surface)
        {
            var swapChainSupport = VkHelper.QuerySwapChainSupport(context.Device.PhysicalDevice, surface);
            var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
            var presentMode = ChooseSwapPresentMode(swapChainSupport.PresentModes);
            var extent = ChooseSwapExtent(swapChainSupport.Capabilities, (uint)surface.Size.X, (uint)surface.Size.Y);

            uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
            if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
            {
                imageCount = swapChainSupport.Capabilities.MaxImageCount;
            }

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

            SetImageSharingMode(context, ref createInfo);

            var swapchainApi = new KhrSwapchain(RenderingContext.Vk.Context);
            swapchainApi.CreateSwapchain(context.Device, in createInfo, in RenderingContext.CustomAllocator<VkSwapchain>(), out var swapChain)
                .VkAssertResult("Failed to create swapchain");

            uint countImages = 0;
            swapchainApi.GetSwapchainImages(context.Device, swapChain, &countImages, null);
            var images = new Image[countImages];
            swapchainApi.GetSwapchainImages(context.Device, swapChain, &countImages, images);

            return new VkSwapchain(context, swapChain, swapchainApi, images, surfaceFormat.Format, extent, surface);
        }

        private unsafe static void SetImageSharingMode(RenderingContext context, ref SwapchainCreateInfoKHR createInfo)
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

        private static PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] availablePresentModes)
        {
            foreach (var availablePresentMode in availablePresentModes)
            {
                if (availablePresentMode == PresentModeKHR.MailboxKhr)
                {
                    return availablePresentMode;
                }
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

        private void FillImageViews(RenderingContext context, Image[] images, Format format, VkImageView[] swapChainImageViews)
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
                swapChainImageViews[i] = VkImageView.Create(context, in ci);
            }
        }


        private unsafe void FillFramebuffers(RenderingContext context, VkImageView[][] framebufferAttachments, VkRenderPass renderPass, Extent2D extent, Image[] images, VkFramebuffer[] swapchainFramebuffers)
        {
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                var attachment = framebufferAttachments[i].Select((s) => s.VkObjectNative).ToArray().AsSpan();
                fixed (ImageView* pImageViews = attachment)
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
                    swapchainFramebuffers[i] = VkFramebuffer.Create(context, in ci);
                }
            }
        }

        public Result AcquireNextImage(ref uint imageIndex)
        {
            var currentFrame = _frameData[_currentFrame];
            var fence = currentFrame.InFlightFence.VkObjectNative;

            // Wait for the previous frame to finish
            RenderingContext.Vk.WaitForFences(_context.Device.VkObjectNative, 1, in fence, true, ulong.MaxValue);

            // Reset the fence for the current frame
            RenderingContext.Vk.ResetFences(_context.Device.VkObjectNative, 1, in fence);

            // Acquire the next image
            return _khrSwapchain.AcquireNextImage(
                _context.Device.VkObjectNative,
                _vkObject,
                ulong.MaxValue,
                currentFrame.ImageAvailableSemaphore,
                default,
                ref imageIndex);

        }


        public unsafe void SubmitCommandBuffers(CommandBuffer[] buffers, uint imageIndex)
        {
            var currentFrame = _frameData[_currentFrame];
            var signalSemaphores = currentFrame.RenderFinishedSemaphore.VkObjectNative;
            var waitSemaphores = currentFrame.ImageAvailableSemaphore.VkObjectNative;
            var currentFence = currentFrame.InFlightFence.VkObjectNative;
            var pStageFlag = PipelineStageFlags.ColorAttachmentOutputBit;

            fixed (CommandBuffer* pcBuffers = buffers)
            {
                var submitInfo = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = &waitSemaphores,
                    PWaitDstStageMask = &pStageFlag,
                    CommandBufferCount = 1,
                    PCommandBuffers = pcBuffers,
                    SignalSemaphoreCount = 1,
                    PSignalSemaphores = &signalSemaphores
                };

                RenderingContext.Vk.QueueSubmit(_context.Device.GraphicsQueue, 1, in submitInfo, currentFence)
                    .VkAssertResult("failed to submit draw command buffer!");

            }
            fixed (SwapchainKHR* pswapchain = &_vkObject)
            {
                var presentInfo = new PresentInfoKHR
                {
                    SType = StructureType.PresentInfoKhr,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = &signalSemaphores,
                    SwapchainCount = 1,
                    PSwapchains = pswapchain,
                    PImageIndices = &imageIndex
                };

               var result =  _khrSwapchain.QueuePresent(_context.Device.PresentQueue, in presentInfo)
                    .VkAssertResult("Failed to present queue", Result.ErrorOutOfDateKhr);
                if (result == Result.ErrorOutOfDateKhr)
                {
                    RecreateSwapchain();
                }
            }
            _currentFrame = (_currentFrame + 1) % _context.MaxFramesPerFlight;
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
                var presentMode = ChooseSwapPresentMode(swapChainSupport.PresentModes);
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

                SetImageSharingMode(_context, ref createInfo);

                _khrSwapchain.CreateSwapchain(_context.Device, in createInfo, in RenderingContext.CustomAllocator<VkSwapchain>(), out var swapChain)
                    .VkAssertResult("Failed to create swapchain");

                uint countImages = 0;
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, ref countImages, null);
                var images = new Image[countImages];
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, &countImages, images);
                _khrSwapchain.DestroySwapchain(_context.Device, oldSwapchain, in RenderingContext.CustomAllocator<VkSwapchain>());
                _vkObject = swapChain;
                _images = images;
                _extent = extent;

                // Recreate image views and framebuffers
                InitializeSwapchainResources();
                _currentFrame = 0;
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
            using var commandPool =  VkCommandPool.Create(_context, new CommandPoolCreateInfo()
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

            var depthImage = VkImage.Create(_context, in imageCi, MemoryPropertyFlags.DeviceLocalBit);
            _depthImage = depthImage;

            commandBuffer.BeginSingleTimeCommand();
            depthImage.TransitionImageLayout(commandBuffer, _depthFormat, ImageLayout.DepthStencilAttachmentOptimal);
            commandBuffer.End();

            var nativeBuffer = commandBuffer.VkObjectNative;
            _context.Device.GraphicsQueue.Submit(new SubmitInfo(StructureType.SubmitInfo) { CommandBufferCount = 1, PCommandBuffers = &nativeBuffer }, fence);
            fence.Wait();

            var imageViewCi = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = depthImage,
                Format = _depthFormat,
                ViewType = ImageViewType.Type2D,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };
            _depthImageView = VkImageView.Create(_context, in imageViewCi);
        }

        private Format FindDepthFormat()
            => FindSupportedFormat([Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint], ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);

        private Format FindSupportedFormat(Format[] candidates, ImageTiling tiling, FormatFeatureFlags features)
        {
            foreach (var format in candidates)
            {
                var properties = RenderingContext.Vk.GetPhysicalDeviceFormatProperties(_context.Device.PhysicalDevice, format);

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

        private class FrameData
        {
            public VkSemaphore ImageAvailableSemaphore { get; }
            public VkSemaphore RenderFinishedSemaphore { get; }
            public VkFence InFlightFence { get; }

            public FrameData(RenderingContext context)
            {
                ImageAvailableSemaphore = VkSemaphore.Create(context);
                RenderFinishedSemaphore = VkSemaphore.Create(context);

                var fenceInfo = new FenceCreateInfo
                {
                    Flags = FenceCreateFlags.SignaledBit,
                    SType = StructureType.FenceCreateInfo
                };
                InFlightFence = VkFence.Create(context, in fenceInfo);
            }

            public void Dispose()
            {
                ImageAvailableSemaphore.Dispose();
                RenderFinishedSemaphore.Dispose();
                InFlightFence.Dispose();
            }
        }

        protected unsafe override void Dispose(bool disposing)
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

            _khrSwapchain.DestroySwapchain(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkSwapchain>());
            _vkObject = default;
            _disposed = true;
        }
    }
}
