using NLog;

using Silk.NET.Vulkan;

using System.Diagnostics;

namespace RockEngine.Vulkan
{
    public sealed class VkImage : VkObject<Image>
    {
        private readonly VulkanContext _context;
        private VkDeviceMemory _imageMemory;
        private ImageCreateInfo _createInfo;
        private uint _currentQueueFamily;
        private readonly ImageAspectFlags _aspectFlags;
        private readonly ImageLayout[,] _layerMipLayouts;

        public VkDeviceMemory ImageMemory => _imageMemory;
        public Format Format => _createInfo.Format;
        public uint MipLevels => _createInfo.MipLevels;
        public Extent3D Extent => _createInfo.Extent;
        public ImageAspectFlags AspectFlags => _aspectFlags;
        public uint ArrayLayers => _createInfo.ArrayLayers;
        public ImageUsageFlags Usage=> _createInfo.Usage;

        public ImageCreateInfo CreateInfo { get => _createInfo; private set => _createInfo = value; }

        public event Action<VkImage>? OnImageResized;
        private readonly Dictionary<(ImageAspectFlags, uint, uint, uint, uint), VkImageView> _viewsCache = new();
        private static uint _id = 0;
        private readonly static Logger _logger = LogManager.GetCurrentClassLogger();

        public VkImage(
            VulkanContext context,
            Image handle,
            VkDeviceMemory memory,
            ImageCreateInfo createInfo,
            ImageAspectFlags aspectFlags)
            : base(handle)
        {
            _context = context;
            _imageMemory = memory;
            _createInfo = createInfo;
            _aspectFlags = aspectFlags;
            _layerMipLayouts = new ImageLayout[createInfo.MipLevels, createInfo.ArrayLayers];
            for (uint mip = 0; mip < createInfo.MipLevels; mip++)
            {
                for (uint layer = 0; layer < createInfo.ArrayLayers; layer++)
                {
                    _layerMipLayouts[mip, layer] = createInfo.InitialLayout;
                }
            }
            LabelObject($"Image ({_id++})");
            if(_imageMemory is not null)
            {
                VulkanAllocator.DeviceMemoryTracker.AssociateObject(
                 _imageMemory,
                 handle.Handle,
                 "Image",
                 memory.Size,
                 0, // offset
                 handle);
            }
        }

        public static VkImage Create(
            VulkanContext context,
            in ImageCreateInfo createInfo,
            MemoryPropertyFlags memoryProperties,
            ImageAspectFlags aspectFlags)
        {
            var image = CreateImage(context, createInfo);
            var memory = AllocateAndBindMemory(context, image, memoryProperties);
            return new VkImage(context, image, memory, createInfo, aspectFlags);
        }

        public static VkImage Create(
          VulkanContext context,
          uint width,
          uint height,
          Format format,
          ImageTiling tiling,
          ImageUsageFlags usage,
          MemoryPropertyFlags memProperties,
          ImageLayout initialLayout = ImageLayout.Undefined,
          uint mipLevels = 1,
          uint arrayLayers = 1,
          SampleCountFlags samples = SampleCountFlags.Count1Bit,
          ImageAspectFlags aspectFlags = ImageAspectFlags.ColorBit)
        {
            var imageCreateInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(width, height, 1),
                MipLevels = mipLevels,
                ArrayLayers = arrayLayers,
                Samples = samples,
                Tiling = tiling,
                Usage = usage,
                InitialLayout = initialLayout,
                SharingMode = SharingMode.Exclusive
            };

            return Create(context, imageCreateInfo, memProperties,aspectFlags);
        }

        private static unsafe Image CreateImage(VulkanContext context, in ImageCreateInfo createInfo)
        {
            Image image;
            VulkanContext.Vk.CreateImage(context.Device, in createInfo, in VulkanContext.CustomAllocator<VkImage>(), &image)
                .VkAssertResult("Failed to create image");
            return image;
        }

        private static VkDeviceMemory AllocateAndBindMemory(
            VulkanContext context,
            Image image,
            MemoryPropertyFlags memoryProperties)
        {
            VulkanContext.Vk.GetImageMemoryRequirements(context.Device, image, out var requirements);
            var memory = VkDeviceMemory.Allocate(context, requirements, memoryProperties);
            VulkanContext.Vk.BindImageMemory(context.Device, image, memory, 0);
            return memory;
        }

        public void Resize(Extent3D newExtent, uint? newArrayLayers = null)
        {
            DisposeResources();
            _createInfo.Extent = newExtent;

            _createInfo.ArrayLayers = newArrayLayers ?? _createInfo.ArrayLayers;

            var newImage = CreateImage(_context, in _createInfo);
            var newMemory = AllocateAndBindMemory(_context, newImage, MemoryPropertyFlags.DeviceLocalBit);

            UpdateResources(newImage, newMemory);
            TransitionToDefaultLayout();

            OnImageResized?.Invoke(this);
        }

        private void UpdateResources(Image newImage, VkDeviceMemory newMemory)
        {
            _vkObject = newImage;
            _imageMemory?.Dispose();
            _imageMemory = newMemory;
        }

        private void TransitionToDefaultLayout()
        {
            ImageLayout targetLayout;
            if (_aspectFlags.HasFlag( ImageAspectFlags.DepthBit))
            {
                Debug.Write("Skipping TransitionToDefaultLayout for Depth texture, you have to manually transition that");
                return;
            }
            else
            {
                targetLayout = ImageLayout.ColorAttachmentOptimal ;
            }
            var batch = _context.GraphicsSubmitContext.CreateBatch();
            batch.CommandBuffer.LabelObject("VKImage.TransitionToDefaultLayout cmd");
            TransitionImageLayout(batch, ImageLayout.Undefined, targetLayout);
            batch.Submit();

        }

        public VkImageView GetOrCreateView(
           ImageAspectFlags aspectFlags,
           uint baseMipLevel = 0,
           uint levelCount = 1,
           uint baseArrayLayer = 0,
           uint layerCount = 1)
        {
            var key = (aspectFlags, baseMipLevel, levelCount, baseArrayLayer, layerCount);

            if (!_viewsCache.TryGetValue(key, out var view))
            {
                view = CreateView(aspectFlags, baseMipLevel, levelCount, baseArrayLayer, layerCount);
                _viewsCache[key] = view;
            }

            return view;
        }

        internal void RemoveViewFromCache(VkImageView view)
        {
            var key = (view.AspectFlags, view.BaseMipLevel, view.LevelCount, view.BaseArrayLayer, view.LayerCount);
            _viewsCache.Remove(key);
        }
        private VkImageView CreateView(
            ImageAspectFlags aspectFlags,
            uint baseMipLevel = 0,
            uint levelCount = 1,
            uint baseArrayLayer = 0,
            uint layerCount = 1)
        {
            ImageViewType viewType;
            if (layerCount == Vk.RemainingArrayLayers)
            {
                layerCount = _createInfo.ArrayLayers - baseArrayLayer;
            }
            // Determine the correct view type based on image properties and layer count
            if (_createInfo.Flags.HasFlag(ImageCreateFlags.CreateCubeCompatibleBit))
            {
                
                if(layerCount % 6 == 0 && layerCount >= 6)
                {
                    // Cube array (multiple cubes)
                    viewType = layerCount > 6 ? ImageViewType.TypeCubeArray : ImageViewType.TypeCube;
                }
                else
                {
                    // Individual cube faces or partial cube
                    viewType = layerCount > 1 ? ImageViewType.Type2DArray : ImageViewType.Type2D;
                }
            }
            else
            {
                // Regular 2D texture - use array type if multiple layers
                viewType = layerCount > 1 ? ImageViewType.Type2DArray : ImageViewType.Type2D;
            }

            return VkImageView.Create(
                _context,
                this,
                Format,
                aspectFlags,
                viewType,
                baseMipLevel,
                levelCount,
                baseArrayLayer,
                layerCount);
        }

        public void TransitionImageLayout(
              UploadBatch batch,
              ImageLayout oldLayout,
              ImageLayout newLayout,
              uint baseMipLevel = 0,
              uint levelCount = Vk.RemainingMipLevels,
              uint baseArrayLayer = 0,
              uint layerCount = Vk.RemainingArrayLayers,
              uint srcQueueFamilyIndex = Vk.QueueFamilyIgnored,
              uint dstQueueFamilyIndex = Vk.QueueFamilyIgnored)
        {
            
            var barrier = new ImageMemoryBarrier2
            {
                SType = StructureType.ImageMemoryBarrier2,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                Image = _vkObject,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = AspectFlags,
                    BaseMipLevel = baseMipLevel,
                    LevelCount = levelCount,
                    BaseArrayLayer = baseArrayLayer,
                    LayerCount = layerCount
                },
                SrcQueueFamilyIndex = srcQueueFamilyIndex,
                DstQueueFamilyIndex = dstQueueFamilyIndex,

            };

            (barrier.SrcAccessMask, barrier.DstAccessMask) = GetAccessMasks(oldLayout, newLayout, srcQueueFamilyIndex, dstQueueFamilyIndex);
            (barrier.SrcStageMask, barrier.DstStageMask) = GetPipelineStages(oldLayout, newLayout, srcQueueFamilyIndex, dstQueueFamilyIndex);

            batch.PipelineBarrier([],[], [barrier], DependencyFlags.ByRegionBit);
        }
        public void GetMemoryBarrier(
              out ImageMemoryBarrier2 barrier,
              ImageLayout oldLayout,
              ImageLayout newLayout,
              uint baseMipLevel = 0,
              uint levelCount = 1,
              uint baseArrayLayer = 0,
              uint layerCount = 1,
              uint srcQueueFamilyIndex = Vk.QueueFamilyIgnored,
              uint dstQueueFamilyIndex = Vk.QueueFamilyIgnored)
        {
            barrier = new ImageMemoryBarrier2
            {
                SType = StructureType.ImageMemoryBarrier2,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                Image = _vkObject,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = AspectFlags,
                    BaseMipLevel = baseMipLevel,
                    LevelCount = levelCount,
                    BaseArrayLayer = baseArrayLayer,
                    LayerCount = layerCount
                },
                SrcQueueFamilyIndex = srcQueueFamilyIndex,
                DstQueueFamilyIndex = dstQueueFamilyIndex

            };

            (barrier.SrcAccessMask, barrier.DstAccessMask) = GetAccessMasks(oldLayout, newLayout, srcQueueFamilyIndex, dstQueueFamilyIndex);
            (barrier.SrcStageMask, barrier.DstStageMask) = GetPipelineStages(oldLayout, newLayout, srcQueueFamilyIndex, dstQueueFamilyIndex);
        }

        public void TransitionImageLayout(
             UploadBatch batch,
             ImageLayout newLayout,
             uint baseMipLevel,
             uint levelCount,
             uint baseArrayLayer,
             uint layerCount,
             uint srcQueueFamilyIndex,
             uint dstQueueFamilyIndex,
             AccessFlags2 srcAccessMask,
             AccessFlags2 dstAccessMask)
        {

            ImageLayout oldLayout = GetMipLayout(baseMipLevel, baseArrayLayer);
            if (oldLayout == newLayout)
            {
                _logger.Warn("Attempt to transition to same layout as it was");
                return;
            }

            var barrier = new ImageMemoryBarrier2
            {
                SType = StructureType.ImageMemoryBarrier2,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                Image = _vkObject,
                SrcAccessMask = srcAccessMask,
                DstAccessMask = dstAccessMask,
                SrcQueueFamilyIndex = srcQueueFamilyIndex,
                DstQueueFamilyIndex = dstQueueFamilyIndex,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = AspectFlags,
                    BaseMipLevel = baseMipLevel,
                    LevelCount = levelCount,
                    BaseArrayLayer = baseArrayLayer,
                    LayerCount = layerCount
                }
            };

            unsafe
            {
                batch.PipelineBarrier([], [],[barrier]);
            }


        }


        public bool GenerateMipmaps(UploadBatch commandBuffer)
        {
            if (!ValidateMipmapGeneration())
            {
                return false;
            }

            // Get total layers (6 for cube maps)
            uint layerCount = _createInfo.ArrayLayers;

            for (uint layer = 0; layer < layerCount; layer++)
            {
                int mipWidth = (int)Extent.Width;
                int mipHeight = (int)Extent.Height;

                for (uint i = 1; i < MipLevels; i++)
                {
                    // Transition destination mip (i) of current layer to TransferDstOptimal
                    TransitionMipLevel(commandBuffer, i, ImageLayout.TransferDstOptimal, layer);

                    // Transition source mip (i-1) of current layer to TransferSrcOptimal
                    TransitionMipLevel(commandBuffer, i - 1, ImageLayout.TransferSrcOptimal, layer);

                    // Blit between mip levels for the current layer
                    var blit = CreateBlitInfo(i, ref mipWidth, ref mipHeight, layer);
                    BlitMipLevel(commandBuffer, blit);

                    // Transition source mip (i-1) to ShaderReadOnlyOptimal
                    TransitionMipLevel(commandBuffer, i - 1, ImageLayout.ShaderReadOnlyOptimal, layer);
                }

                // Transition final mip level to ShaderReadOnlyOptimal
                TransitionMipLevel(
                       commandBuffer,
                       mipLevel: MipLevels - 1,
                       newLayout: ImageLayout.ShaderReadOnlyOptimal,
                       layer: layer);
            }
            return true;
        }

        private bool ValidateMipmapGeneration()
        {
            var formatProperties = _context.Device.PhysicalDevice.GetFormatProperties(Format);
            bool result =  ((formatProperties.OptimalTilingFeatures & FormatFeatureFlags.BlitSrcBit) != 0 &&
                (formatProperties.OptimalTilingFeatures & FormatFeatureFlags.BlitDstBit) != 0);
            if (!result)
            {
                _logger.Warn($"Format {Format}  doesn't support blitting!");
            }
            return result;
           
        }

        private void BlitMipLevel(UploadBatch batch, in ImageBlit blit)
        {
            VulkanContext.Vk.CmdBlitImage(
                batch.CommandBuffer,
                _vkObject, ImageLayout.TransferSrcOptimal,
                _vkObject, ImageLayout.TransferDstOptimal,
                1, in blit,
                Filter.Linear);
        }

        private static ImageBlit CreateBlitInfo(
             uint currentMip,
             ref int mipWidth,
             ref int mipHeight,
             uint layer)
        {
            int srcWidth = mipWidth;
            int srcHeight = mipHeight;

            mipWidth = Math.Max(srcWidth / 2, 1);
            mipHeight = Math.Max(srcHeight / 2, 1);

            return new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = currentMip - 1,
                    BaseArrayLayer = layer, // Source layer
                    LayerCount = 1
                },
                SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                {
                    [0] = new Offset3D(0, 0, 0),
                    [1] = new Offset3D(srcWidth, srcHeight, 1)
                },
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = currentMip,
                    BaseArrayLayer = layer, // Destination layer
                    LayerCount = 1
                },
                DstOffsets = new ImageBlit.DstOffsetsBuffer
                {
                    [0] = new Offset3D(0, 0, 0),
                    [1] = new Offset3D(mipWidth, mipHeight, 1)
                }
            };
        }

        private void TransitionMipLevel(
            UploadBatch batch,
            uint mipLevel,
            ImageLayout newLayout,
            uint layer)
        {
            // Get current layout for THIS mip+layer
            ImageLayout oldLayout = GetMipLayout(mipLevel, layer);
            if (oldLayout == newLayout)
            {
                return;
            }

            var (srcAccess, dstAccess) = GetAccessMasks(oldLayout, newLayout, Vk.QueueFamilyIgnored, Vk.QueueFamilyIgnored);

            var barrier = new ImageMemoryBarrier2
            {
                SType = StructureType.ImageMemoryBarrier2,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                Image = _vkObject,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = AspectFlags,
                    BaseMipLevel = mipLevel,
                    LevelCount = 1,
                    BaseArrayLayer = layer, // Target specific layer
                    LayerCount = 1
                },
                SrcAccessMask = srcAccess,
                DstAccessMask = dstAccess
            };
            (barrier.SrcStageMask, barrier.DstStageMask) = GetPipelineStages(oldLayout, newLayout,Vk.QueueFamilyIgnored, Vk.QueueFamilyIgnored);


            batch.PipelineBarrier([],[], [barrier]);
            batch.AddDependency(new DeferredOperation(() =>
            {
                // Update tracked layout for THIS mip+layer
                SetMipLayout(mipLevel, newLayout, layer);
            }));
          
        }

        protected override void Dispose(bool disposing)
        {
            if (IsDisposed)
            {
                return;
            }
            if (_imageMemory is null)
            {
                _vkObject = default;
                _imageMemory = null!;
                // MOST LIKELY WE ARE the swapchain image
                return;
            }
            VulkanAllocator.DeviceMemoryTracker.DisassociateObject(_vkObject.Handle);

            VulkanContext.Vk.DestroyImage(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkImage>());
            _imageMemory?.Dispose();

        }
        private void DisposeResources()
        {
            if (_vkObject.Handle != default)
            {
                VulkanContext.Vk.DestroyImage(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkImage>());
            }
            _imageMemory?.Dispose();

            _vkObject = default;
            _imageMemory = null!;
        }

        public ImageLayout GetMipLayout(uint mipLevel, uint layer = 0)
        {
            return _layerMipLayouts[mipLevel, layer];
        }

        internal void SetMipLayout(uint mipLevel, ImageLayout layout, uint layer = 0)
            => _layerMipLayouts[mipLevel, layer] = layout;


        private (PipelineStageFlags2 oldStage, PipelineStageFlags2 newStage) GetPipelineStages(ImageLayout oldLayout, ImageLayout newLayout, uint srcQueueFamilyIndex, uint dstQueueFamilyIndex)
        {
            // Handle queue family ownership transfers
            if (srcQueueFamilyIndex != dstQueueFamilyIndex)
            {
                // Queue family ownership transfer
                return (oldLayout, newLayout) switch
                {
                    // Release from source queue
                    (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.General)
                        => (PipelineStageFlags2.BottomOfPipeBit, PipelineStageFlags2.TopOfPipeBit),
                    // Acquire by destination queue
                    (ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal)
                        => (PipelineStageFlags2.BottomOfPipeBit, PipelineStageFlags2.ComputeShaderBit),
                    (ImageLayout.Undefined, ImageLayout.General) =>
                        (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.ComputeShaderBit),
                    // Add PresentSrcKhr transitions for queue family transfer
                    (ImageLayout.PresentSrcKhr, ImageLayout.General)
                        => (PipelineStageFlags2.BottomOfPipeBit, PipelineStageFlags2.TopOfPipeBit),
                    (ImageLayout.General, ImageLayout.PresentSrcKhr)
                        => (PipelineStageFlags2.BottomOfPipeBit, PipelineStageFlags2.TopOfPipeBit),
                    _ => throw new NotSupportedException($"Unsupported queue family transition: {oldLayout} -> {newLayout}")
                };
            }

            return (oldLayout, newLayout) switch
            {
                // Undefined -> PresentSrcKhr (for initial setup)
                (ImageLayout.Undefined, ImageLayout.PresentSrcKhr)
                    => (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.BottomOfPipeBit),

                // ColorAttachmentOptimal -> PresentSrcKhr (after rendering)
                (ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr)
                    => (PipelineStageFlags2.ColorAttachmentOutputBit, PipelineStageFlags2.BottomOfPipeBit),

                // PresentSrcKhr -> ColorAttachmentOptimal (for next frame)
                (ImageLayout.PresentSrcKhr, ImageLayout.ColorAttachmentOptimal)
                    => (PipelineStageFlags2.BottomOfPipeBit, PipelineStageFlags2.ColorAttachmentOutputBit),

                // PresentSrcKhr -> Undefined (for recreation)
                (ImageLayout.PresentSrcKhr, ImageLayout.Undefined)
                    => (PipelineStageFlags2.BottomOfPipeBit, PipelineStageFlags2.TopOfPipeBit),

                // PresentSrcKhr -> TransferSrcOptimal (for screenshot/etc)
                (ImageLayout.PresentSrcKhr, ImageLayout.TransferSrcOptimal)
                    => (PipelineStageFlags2.BottomOfPipeBit, PipelineStageFlags2.TransferBit),

                // TransferSrcOptimal -> PresentSrcKhr
                (ImageLayout.TransferSrcOptimal, ImageLayout.PresentSrcKhr)
                    => (PipelineStageFlags2.TransferBit, PipelineStageFlags2.BottomOfPipeBit),

                // Add Undefined -> Depth/Stencil ReadOnly transitions
                (ImageLayout.Undefined, ImageLayout.DepthStencilReadOnlyOptimal)
                    => (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.FragmentShaderBit),
                (ImageLayout.Undefined, ImageLayout.DepthReadOnlyOptimal)
                    => (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.FragmentShaderBit),
                (ImageLayout.TransferSrcOptimal, ImageLayout.DepthStencilAttachmentOptimal)
                    => (PipelineStageFlags2.TransferBit, PipelineStageFlags2.LateFragmentTestsBit),

                // And the reverse
                (ImageLayout.DepthStencilAttachmentOptimal, ImageLayout.TransferSrcOptimal)
                    => (PipelineStageFlags2.LateFragmentTestsBit, PipelineStageFlags2.TransferBit),

                // Add Depth/Stencil attachment -> ReadOnly transitions
                (ImageLayout.DepthStencilAttachmentOptimal, ImageLayout.DepthStencilReadOnlyOptimal)
                    => (PipelineStageFlags2.LateFragmentTestsBit, PipelineStageFlags2.FragmentShaderBit),
                (ImageLayout.DepthStencilReadOnlyOptimal, ImageLayout.DepthStencilAttachmentOptimal)
                    => (PipelineStageFlags2.FragmentShaderBit, PipelineStageFlags2.LateFragmentTestsBit),
                (ImageLayout.DepthAttachmentOptimal, ImageLayout.DepthReadOnlyOptimal)
                    => (PipelineStageFlags2.LateFragmentTestsBit, PipelineStageFlags2.FragmentShaderBit),
                (ImageLayout.DepthReadOnlyOptimal, ImageLayout.DepthAttachmentOptimal)
                    => (PipelineStageFlags2.FragmentShaderBit, PipelineStageFlags2.LateFragmentTestsBit),

                // Existing transitions...
                (ImageLayout.General, ImageLayout.TransferSrcOptimal)
                    => (PipelineStageFlags2.ComputeShaderBit, PipelineStageFlags2.TransferBit),
                // ShaderReadOnlyOptimal → General (e.g., for compute write)
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.General)
                    => (PipelineStageFlags2.FragmentShaderBit, PipelineStageFlags2.ComputeShaderBit),
                // General → ShaderReadOnlyOptimal (e.g., after compute write)
                (ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal)
                    => (PipelineStageFlags2.ComputeShaderBit, PipelineStageFlags2.FragmentShaderBit),

                // ColorAttachmentOptimal → TransferSrcOptimal (for picking)
                (ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal)
                    => (PipelineStageFlags2.ColorAttachmentOutputBit, PipelineStageFlags2.TransferBit),

                // TransferSrcOptimal → ColorAttachmentOptimal (reverse transition)
                (ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal)
                    => (PipelineStageFlags2.TransferBit, PipelineStageFlags2.ColorAttachmentOutputBit),

                // ColorAttachmentOptimal → ShaderReadOnlyOptimal
                (ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal)
                    => (PipelineStageFlags2.ColorAttachmentOutputBit, PipelineStageFlags2.FragmentShaderBit),

                // ShaderReadOnlyOptimal → ColorAttachmentOptimal
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ColorAttachmentOptimal)
                    => (PipelineStageFlags2.FragmentShaderBit, PipelineStageFlags2.ColorAttachmentOutputBit),

                // Existing transitions
                (ImageLayout.Undefined, ImageLayout.TransferDstOptimal) =>
                    (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.TransferBit),
                (ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (PipelineStageFlags2.TransferBit, PipelineStageFlags2.FragmentShaderBit),
                (ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (PipelineStageFlags2.TransferBit, PipelineStageFlags2.FragmentShaderBit),
                (ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal) =>
                    (PipelineStageFlags2.TransferBit, PipelineStageFlags2.TransferBit),
                (ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal) =>
                    (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.ColorAttachmentOutputBit),

                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal) =>
                    (PipelineStageFlags2.FragmentShaderBit, PipelineStageFlags2.TransferBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferSrcOptimal) =>
                    (PipelineStageFlags2.FragmentShaderBit, PipelineStageFlags2.TransferBit),
                (ImageLayout.Undefined, ImageLayout.TransferSrcOptimal) =>
                    (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.TransferBit),
                (ImageLayout.Undefined, ImageLayout.General) =>
                    (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.ComputeShaderBit),

                // Add depth/stencil transitions
                (ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal) =>
                    (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.EarlyFragmentTestsBit),
                (ImageLayout.Undefined, ImageLayout.DepthAttachmentOptimal) =>
                    (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.EarlyFragmentTestsBit),

                (ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal) =>
                    (PipelineStageFlags2.TopOfPipeBit, PipelineStageFlags2.FragmentShaderBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                (PipelineStageFlags2.FragmentShaderBit, PipelineStageFlags2.FragmentShaderBit),


                _ => throw new NotSupportedException($"Unsupported layout transition: {oldLayout} -> {newLayout}")
            };
        }
        private (AccessFlags2 srcAccessFlag, AccessFlags2 dstAccessFlag) GetAccessMasks(ImageLayout oldLayout, ImageLayout newLayout, uint srcQueueFamilyIndex, uint dstQueueFamilyIndex)
        {
            if (srcQueueFamilyIndex != dstQueueFamilyIndex)
            {
                return (AccessFlags2.None, AccessFlags2.None);
            }
            return (oldLayout, newLayout) switch
            {
                // ADD THESE MISSING TRANSITIONS FOR PresentSrcKhr:
                // Undefined -> PresentSrcKhr
                (ImageLayout.Undefined, ImageLayout.PresentSrcKhr)
                    => (AccessFlags2.None, AccessFlags2.MemoryReadBit),

                // ColorAttachmentOptimal -> PresentSrcKhr
                (ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr)
                    => (AccessFlags2.ColorAttachmentWriteBit, AccessFlags2.MemoryReadBit),

                // PresentSrcKhr -> ColorAttachmentOptimal
                (ImageLayout.PresentSrcKhr, ImageLayout.ColorAttachmentOptimal)
                    => (AccessFlags2.MemoryReadBit, AccessFlags2.ColorAttachmentWriteBit),

                // PresentSrcKhr -> Undefined
                (ImageLayout.PresentSrcKhr, ImageLayout.Undefined)
                    => (AccessFlags2.MemoryReadBit, AccessFlags2.None),

                // PresentSrcKhr -> TransferSrcOptimal
                (ImageLayout.PresentSrcKhr, ImageLayout.TransferSrcOptimal)
                    => (AccessFlags2.MemoryReadBit, AccessFlags2.TransferReadBit),

                // TransferSrcOptimal -> PresentSrcKhr
                (ImageLayout.TransferSrcOptimal, ImageLayout.PresentSrcKhr)
                    => (AccessFlags2.TransferReadBit, AccessFlags2.MemoryReadBit),

                // Add Undefined -> Depth/Stencil ReadOnly access masks
                (ImageLayout.Undefined, ImageLayout.DepthStencilReadOnlyOptimal)
                    => (AccessFlags2.None, AccessFlags2.ShaderReadBit),
                (ImageLayout.Undefined, ImageLayout.DepthReadOnlyOptimal)
                    => (AccessFlags2.None, AccessFlags2.ShaderReadBit),

                // Add the missing transition that's causing the error
                (ImageLayout.TransferSrcOptimal, ImageLayout.DepthStencilAttachmentOptimal)
                    => (AccessFlags2.TransferReadBit, AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit),

                // Also add the reverse transition for completeness
                (ImageLayout.DepthStencilAttachmentOptimal, ImageLayout.TransferSrcOptimal)
                    => (AccessFlags2.DepthStencilAttachmentWriteBit, AccessFlags2.TransferReadBit),

                // Add Depth/Stencil attachment -> ReadOnly access masks
                (ImageLayout.DepthStencilAttachmentOptimal, ImageLayout.DepthStencilReadOnlyOptimal)
                    => (AccessFlags2.DepthStencilAttachmentWriteBit, AccessFlags2.ShaderReadBit),
                (ImageLayout.DepthStencilReadOnlyOptimal, ImageLayout.DepthStencilAttachmentOptimal)
                    => (AccessFlags2.ShaderReadBit, AccessFlags2.DepthStencilAttachmentWriteBit),
                (ImageLayout.DepthAttachmentOptimal, ImageLayout.DepthReadOnlyOptimal)
                    => (AccessFlags2.DepthStencilAttachmentWriteBit, AccessFlags2.ShaderReadBit),
                (ImageLayout.DepthReadOnlyOptimal, ImageLayout.DepthAttachmentOptimal)
                    => (AccessFlags2.ShaderReadBit, AccessFlags2.DepthStencilAttachmentWriteBit),

                // Existing transitions...
                (ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal)
                    => (AccessFlags2.ColorAttachmentWriteBit, AccessFlags2.TransferReadBit),

                // TransferSrcOptimal → ColorAttachmentOptimal
                (ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal)
                    => (AccessFlags2.TransferReadBit, AccessFlags2.ColorAttachmentWriteBit),

                // ColorAttachmentOptimal → ShaderReadOnlyOptimal
                (ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal)
                    => (AccessFlags2.ColorAttachmentWriteBit, AccessFlags2.ShaderReadBit),

                // ShaderReadOnlyOptimal → ColorAttachmentOptimal
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ColorAttachmentOptimal)
                    => (AccessFlags2.ShaderReadBit, AccessFlags2.ColorAttachmentWriteBit),
                (ImageLayout.General, ImageLayout.TransferSrcOptimal)
                    => (AccessFlags2.ShaderWriteBit, AccessFlags2.TransferReadBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.General)
                       => (AccessFlags2.ShaderReadBit, AccessFlags2.ShaderWriteBit),

                (ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal)
                    => (AccessFlags2.ShaderWriteBit, AccessFlags2.ShaderReadBit),
                // Existing transitions
                (ImageLayout.Undefined, ImageLayout.TransferDstOptimal) =>
                    (AccessFlags2.None, AccessFlags2.TransferWriteBit),
                (ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags2.TransferWriteBit, AccessFlags2.ShaderReadBit),
                (ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags2.TransferReadBit, AccessFlags2.ShaderReadBit),
                (ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal) =>
                    (AccessFlags2.TransferWriteBit, AccessFlags2.TransferReadBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal) =>
                    (AccessFlags2.ShaderReadBit, AccessFlags2.TransferWriteBit),

                (ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal) =>
                    (AccessFlags2.None, AccessFlags2.ColorAttachmentWriteBit),

                (ImageLayout.Undefined, ImageLayout.TransferSrcOptimal) =>
                    (AccessFlags2.None, AccessFlags2.TransferReadBit),
                (ImageLayout.Undefined, ImageLayout.General) =>
                    (AccessFlags2.None, AccessFlags2.ShaderWriteBit),
                // Add depth/stencil transitions
                (ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal) =>
                    (AccessFlags2.None, AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit),

                (ImageLayout.Undefined, ImageLayout.DepthAttachmentOptimal) =>
                    (AccessFlags2.None, AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit),
                (ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags2.None, AccessFlags2.ShaderReadBit),

                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferSrcOptimal) =>
                    (AccessFlags2.ShaderReadBit, AccessFlags2.TransferReadBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
               (AccessFlags2.ShaderReadBit, AccessFlags2.ShaderReadBit),

                _ => throw new NotSupportedException($"Unsupported layout transition: {oldLayout} -> {newLayout}")
            };
        }

        public override void LabelObject(string name) { }

        public VkImageView GetMipView(uint mipLevel)
        {
            return GetOrCreateView(
               AspectFlags,
               mipLevel,
               1, // levelCount
               0, // baseArrayLayer
               _createInfo.ArrayLayers  // layerCount
           );
        }
        /// <summary>
        /// Returns a cached image view for the specified mip and layer range.
        /// </summary>
        /// <param name="baseMipLevel">First mip level to include.</param>
        /// <param name="levelCount">Number of mip levels.</param>
        /// <param name="baseArrayLayer">First array layer to include.</param>
        /// <param name="layerCount">Number of array layers.</param>
        /// <returns>A Vulkan image view handle.</returns>
        public VkImageView GetView(uint baseMipLevel = 0, uint levelCount = 1,
                                   uint baseArrayLayer = 0, uint layerCount = 1)
        {
            return GetOrCreateView(AspectFlags, baseMipLevel, levelCount, baseArrayLayer, layerCount);
        }
    }
}
