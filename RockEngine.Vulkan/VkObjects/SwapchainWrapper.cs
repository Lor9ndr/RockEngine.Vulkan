using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;
using Silk.NET.Input;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;


namespace RockEngine.Vulkan.VkObjects
{
    public class SwapchainWrapper : VkObject<SwapchainKHR>
    {
        private readonly KhrSwapchain _khrSwapchain;
        private readonly Format _format;
        private readonly RenderPassWrapper _renderPass;
        private readonly VulkanContext _context;

        private Silk.NET.Vulkan.Image[] _images;
        private Extent2D _extent;
        private readonly ISurfaceHandler _surface;
        private readonly ImageView[] _swapChainImageViews;
        private readonly FramebufferWrapper[] _swapchainFramebuffers;

        private readonly List<SemaphoreWrapper> _imageAvailableSemaphores;
        private readonly List<SemaphoreWrapper> _renderFinishedSemaphores;
        private readonly List<FenceWrapper> _inFlightFences;
        private int _currentFrame = 0;
        private readonly SwapchainWrapper? _oldSwapchain;
        private Image _depthImage;
        private ImageView _depthImageView;
        private Format _depthFormat;

        public KhrSwapchain SwapchainApi => _khrSwapchain;
        public Silk.NET.Vulkan.Image[] Images => _images;
        public Format Format => _format;
        public Extent2D Extent => _extent;
        public ImageView[] SwapChainImageViews => _swapChainImageViews;
        public RenderPassWrapper RenderPass => _renderPass;
        public FramebufferWrapper[] SwapchainFramebuffers => _swapchainFramebuffers;
        public SwapchainWrapper? OldSwapchain => _oldSwapchain;
        public int SwapChainImagesCount => _images.Length;
        public Format DepthFormat => _depthFormat;
        public ISurfaceHandler Surface => _surface;
        public int CurrentFrameIndex => _currentFrame;
        public SemaphoreSlim SwapchainSemaphore = new SemaphoreSlim(1, 1);

        public SwapchainWrapper(VulkanContext context, SwapchainKHR swapchain, KhrSwapchain khrSwapchainApi, Silk.NET.Vulkan.Image[] images, Format format, Extent2D extent, ISurfaceHandler surface)
            : base(swapchain)
        {
            _swapChainImageViews = new ImageView[images.Length];
            _swapchainFramebuffers = new FramebufferWrapper[images.Length];

            _imageAvailableSemaphores = new List<SemaphoreWrapper>(VulkanContext.MAX_FRAMES_IN_FLIGHT);
            _renderFinishedSemaphores = new List<SemaphoreWrapper>(VulkanContext.MAX_FRAMES_IN_FLIGHT);
            _inFlightFences = new List<FenceWrapper>(VulkanContext.MAX_FRAMES_IN_FLIGHT);

            _context = context;
            _khrSwapchain = khrSwapchainApi;
            _images = images;
            _format = format;
            _extent = extent;
            _surface = surface;
            _renderPass = CreateRenderPass();

            InitializeSwapchainResources();
        }

        public unsafe static SwapchainWrapper Create(VulkanContext context, ISurfaceHandler surface, uint width, uint height)
        {
            var swapChainSupport = VkHelper.QuerySwapChainSupport(context.Device.PhysicalDevice, surface);
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
                Surface = surface.Surface,
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

            SetImageSharingMode(context, ref createInfo);

            var swapchainApi = new KhrSwapchain(context.Api.Context);
            swapchainApi.CreateSwapchain(context.Device, in createInfo, null, out var swapChain)
                .ThrowCode("Failed to create swapchain");

            uint countImages = 0;
            swapchainApi.GetSwapchainImages(context.Device, swapChain, &countImages, null);
            var images = new Silk.NET.Vulkan.Image[countImages];
            swapchainApi.GetSwapchainImages(context.Device, swapChain, &countImages, images);

            return new SwapchainWrapper(context, swapChain, swapchainApi, images, surfaceFormat.Format, extent, surface);
        }

        private unsafe static void SetImageSharingMode(VulkanContext context, ref SwapchainCreateInfoKHR createInfo)
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
            FillImageViews(_context, _images, _format, _swapChainImageViews);
            CreateDepthResources();
            CreateFramebuffers();
            CreateSyncObjects();
        }

        private void FillImageViews(VulkanContext context, Silk.NET.Vulkan.Image[] images, Format format, ImageView[] swapChainImageViews)
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
                swapChainImageViews[i] = ImageView.Create(context, in ci);
            }
        }

        private void CreateFramebuffers()
        {
            var framebufferAttachments = new ImageView[_swapChainImageViews.Length][];
            for (int i = 0; i < _swapChainImageViews.Length; i++)
            {
                framebufferAttachments[i] = new ImageView[] { _swapChainImageViews[i], _depthImageView };
            }
            FillFramebuffers(_context, framebufferAttachments, _renderPass, _extent, _images, _swapchainFramebuffers);
        }

        private unsafe void FillFramebuffers(VulkanContext context, ImageView[][] framebufferAttachments, RenderPassWrapper renderPass, Extent2D extent, Silk.NET.Vulkan.Image[] images, FramebufferWrapper[] swapchainFramebuffers)
        {
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                var attachment = framebufferAttachments[i].Select((s)=>s.VkObjectNative).ToArray().AsSpan();
                fixed(Silk.NET.Vulkan.ImageView* pImageViews = attachment)
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
                    swapchainFramebuffers[i] = FramebufferWrapper.Create(context, in ci);
                }
            }
        }

        private void CreateSyncObjects()
        {
            var fenceInfo = new FenceCreateInfo { Flags = FenceCreateFlags.SignaledBit, SType = StructureType.FenceCreateInfo };

            for (int i = 0; i < VulkanContext.MAX_FRAMES_IN_FLIGHT; i++)
            {
                _imageAvailableSemaphores.Add(SemaphoreWrapper.Create(_context));
                _renderFinishedSemaphores.Add(SemaphoreWrapper.Create(_context));
                _inFlightFences.Add(FenceWrapper.Create(_context, in fenceInfo));
            }
        }

        public Result AcquireNextImage(ref uint imageIndex)
        {
            ValidateFenceAndSemaphore();

            return _khrSwapchain.AcquireNextImage(_context.Device.VkObjectNative, _vkObject, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref imageIndex);
        }

        private void ValidateFenceAndSemaphore()
        {
            var fence = _inFlightFences[_currentFrame].VkObjectNative;
            if (fence.Handle == default)
            {
                throw new InvalidOperationException("Fence is not properly initialized.");
            }

            _context.Api.WaitForFences(_context.Device.VkObjectNative, 1, in fence, true, 100_000_000_0);

            var semaphore = _imageAvailableSemaphores[_currentFrame];
            if (semaphore.VkObjectNative.Handle == default)
            {
                throw new InvalidOperationException("Semaphore is not properly initialized.");
            }

            if (_vkObject.Handle == default)
            {
                throw new InvalidOperationException("Swapchain is not properly initialized.");
            }
        }

        public unsafe void SubmitCommandBuffers(CommandBuffer[] buffers, uint imageIndex)
        {
            _context.QueueMutex.WaitOne();
            try
            {
                var imageInflight = _inFlightFences[_currentFrame]!.VkObjectNative;
                var signalSemaphores = stackalloc Semaphore[] { _renderFinishedSemaphores[_currentFrame].VkObjectNative };
                var waitSemaphores = stackalloc Semaphore[] { _imageAvailableSemaphores[_currentFrame].VkObjectNative };
                var currentFence = _inFlightFences[_currentFrame].VkObjectNative;

                _context.Api.WaitForFences(_context.Device.VkObjectNative, 1, in imageInflight, true, uint.MaxValue);
                _context.Api.ResetFences(_context.Device.VkObjectNative, 1, in currentFence);

                var pStageFlag = stackalloc PipelineStageFlags[] { PipelineStageFlags.ColorAttachmentOutputBit };
                fixed (CommandBuffer* pcBuffers = buffers)
                {
                    var submitInfo = new SubmitInfo
                    {
                        SType = StructureType.SubmitInfo,
                        WaitSemaphoreCount = 1,
                        PWaitSemaphores = waitSemaphores,
                        PWaitDstStageMask = pStageFlag,
                        CommandBufferCount = 1,
                        PCommandBuffers = pcBuffers,
                        SignalSemaphoreCount = 1,
                        PSignalSemaphores = signalSemaphores
                    };

                    _context.Api.QueueSubmit(_context.Device.GraphicsQueue, 1, in submitInfo, _inFlightFences[_currentFrame])
                        .ThrowCode("failed to submit draw command buffer!");

                    fixed (SwapchainKHR* pswapchain = &_vkObject)
                    {
                        var presentInfo = new PresentInfoKHR
                        {
                            SType = StructureType.PresentInfoKhr,
                            WaitSemaphoreCount = 1,
                            PWaitSemaphores = signalSemaphores,
                            SwapchainCount = 1,
                            PSwapchains = pswapchain,
                            PImageIndices = &imageIndex
                        };
                        _khrSwapchain.QueuePresent(_context.Device.PresentQueue, in presentInfo);
                        _currentFrame = (_currentFrame + 1) % VulkanContext.MAX_FRAMES_IN_FLIGHT;
                    }
                }
            }
            finally
            {
                _context.QueueMutex.ReleaseMutex();
            }
        }

        public void RecreateSwapchainAsync(ISurfaceHandler surface, uint width, uint height)
        {
            while (_surface.Size.X == 0 || _surface.Size.Y == 0)
            {
                // Wait until the window is not minimized
            }

            _context.Api.DeviceWaitIdle(_context.Device);

            var oldSwapchain = _vkObject;

            DisposeFramebuffersAndImageViews();

            unsafe
            {
                var swapChainSupport = VkHelper.QuerySwapChainSupport(_context.Device.PhysicalDevice, surface);
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
                    Surface = surface.Surface,
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
                    OldSwapchain = _vkObject
                };

                SetImageSharingMode(_context, ref createInfo);

                _khrSwapchain.CreateSwapchain(_context.Device, in createInfo, null, out var swapChain)
                    .ThrowCode("Failed to create swapchain");

                uint countImages = 0;
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, ref countImages, null);
                var images = new Silk.NET.Vulkan.Image[countImages];
                _khrSwapchain.GetSwapchainImages(_context.Device, swapChain, &countImages, images);

                _khrSwapchain.DestroySwapchain(_context.Device, oldSwapchain, null);
                _vkObject = swapChain;
                _images = images;
                _extent = extent;
                InitializeSwapchainResources();
                _currentFrame = 0;
            }
        }

        private void DisposeFramebuffersAndImageViews()
        {
            foreach (var item in _swapchainFramebuffers)
            {
                item.Dispose();
            }
            foreach (var item in _swapChainImageViews)
            {
                item.Dispose();
            }
            _depthImage.Dispose();
            _depthImageView.Dispose();
        }

        private unsafe RenderPassWrapper CreateRenderPass()
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

            return RenderPassWrapper.Create(_context, new[] { description }, new[] { colorAttachment, depthAttachment }, new[] { dependency });
        }

        public void CreateDepthResources()
        {
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

            var depthImage = Image.Create(_context, in imageCi, MemoryPropertyFlags.DeviceLocalBit);
            _depthImage = depthImage;
            depthImage.TransitionImageLayout(_context, _depthFormat, ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal);

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
            _depthImageView = ImageView.Create(_context, in imageViewCi);
        }

        private Format FindDepthFormat()
            => FindSupportedFormat(new[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint }, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);

        private Format FindSupportedFormat(Format[] candidates, ImageTiling tiling, FormatFeatureFlags features)
        {
            foreach (var format in candidates)
            {
                var properties = _context.Api.GetPhysicalDeviceFormatProperties(_context.Device.PhysicalDevice, format);

                if (tiling == ImageTiling.Linear && (properties.LinearTilingFeatures & features) == features)
                {
                    return format;
                }
                else if (tiling == ImageTiling.Optimal && (properties.OptimalTilingFeatures & features) == features)
                {
                    return format;
                }
            }
            throw new InvalidOperationException("Failed to find supported format.");
        }

        protected unsafe override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _renderPass.Dispose();
            DisposeFramebuffersAndImageViews();

            foreach (var item in _imageAvailableSemaphores)
            {
                item.Dispose();
            }

            foreach (var item in _renderFinishedSemaphores)
            {
                item.Dispose();
            }
            
            _khrSwapchain.DestroySwapchain(_context.Device, _vkObject,null);
            _vkObject = default;
            _disposed = true;
        }
    }
}
