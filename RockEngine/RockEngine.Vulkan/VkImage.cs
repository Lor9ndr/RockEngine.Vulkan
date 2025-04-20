using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkImage : VkObject<Image>
    {
        private VkDeviceMemory _imageMemory;
        private readonly VulkanContext _context;
        private ImageCreateInfo _createInfo;

        private ImageLayout _currentLayout;

        private readonly ImageLayout[] _mipLayouts;

        public VkDeviceMemory ImageMemory => _imageMemory;

        public ImageLayout CurrentLayout => _currentLayout;

        public Format Format => _createInfo.Format;

        public uint MipLevels => _createInfo.MipLevels;

        public uint Width => _createInfo.Extent.Width;
        public uint Height => _createInfo.Extent.Height;

        public event Action<VkImage> OnImageResized;


        public VkImage(VulkanContext context, Image vkImage, VkDeviceMemory imageMemory, ImageCreateInfo createInfo)
            : base(vkImage)

        {
            _createInfo = createInfo;
            _imageMemory = imageMemory;
            _currentLayout = createInfo.InitialLayout;
            _context = context;
            _mipLayouts = new ImageLayout[MipLevels];
            Array.Fill(_mipLayouts, ImageLayout.Undefined);
        }

        public static unsafe VkImage Create(VulkanContext context, in ImageCreateInfo ci, MemoryPropertyFlags memPropertyFlags)
        {
            VulkanContext.Vk.CreateImage(context.Device, in ci, in VulkanContext.CustomAllocator<VkImage>(), out var vkImage)
                .VkAssertResult("Failed to create image!");
            VulkanContext.Vk.GetImageMemoryRequirements(context.Device, vkImage, out var memRequirements);
            var imageMemory = VkDeviceMemory.Allocate(context, memRequirements, memPropertyFlags);

            VulkanContext.Vk.BindImageMemory(context.Device, vkImage, imageMemory, 0);
            return new VkImage(context, vkImage, imageMemory, ci);
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
           SampleCountFlags samples = SampleCountFlags.Count1Bit)
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

            return Create(context, imageCreateInfo, memProperties);
        }

        public void Resize(uint newWidth, uint newHeight)
        {
            // Destroy existing resources
            DisposeResources();

            // Create new image with updated size
            _createInfo.Extent = new Extent3D(newWidth, newHeight,1);

            // Create new Vulkan image and memory
            VulkanContext.Vk.CreateImage(_context.Device, in _createInfo, in VulkanContext.CustomAllocator<VkImage>(), out var newImage);
            VulkanContext.Vk.GetImageMemoryRequirements(_context.Device, newImage, out var memReqs);
            var newMemory = VkDeviceMemory.Allocate(_context, memReqs, MemoryPropertyFlags.DeviceLocalBit);
            VulkanContext.Vk.BindImageMemory(_context.Device, newImage, newMemory, 0);

            // Update internal state
            _vkObject = newImage;
            _imageMemory = newMemory;

            // Transition to default layout
            var batch = _context.SubmitContext.CreateBatch();

            _currentLayout = ImageLayout.Undefined;

            TransitionImageLayout(batch.CommandBuffer, ImageLayout.ShaderReadOnlyOptimal);
            batch.Submit();

            // Notify subscribers
            OnImageResized?.Invoke(this);
        }
       
        public unsafe VkImageView CreateView(
            ImageAspectFlags aspectFlags,
            uint mipLevels = 1,
            uint arrayLayers = 1,
            uint baseMipLevel = 0)
        {
            return VkImageView.Create(_context, this, Format, aspectFlags, mipLevels: mipLevels, arrayLayers: arrayLayers, baseMipLevel: baseMipLevel);
        }

        public unsafe void TransitionMipLayout(VkCommandBuffer commandBuffer, ImageLayout newLayout, uint mipLevel)
        {
            ImageLayout oldLayout = GetMipLayout(mipLevel);
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                Image = this,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = mipLevel,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            VulkanContext.Vk.CmdPipelineBarrier(commandBuffer,
                GetSrcStage(oldLayout),
                GetDstStage(newLayout),
                0, 0, null, 0, null, 1, &barrier);
            SetMipLayout(mipLevel, newLayout);
        }

        public ImageLayout GetMipLayout(uint mipLevel) => _mipLayouts[mipLevel];
        public void SetMipLayout(uint mipLevel, ImageLayout layout) => _mipLayouts[mipLevel] = layout;

        private PipelineStageFlags GetSrcStage(ImageLayout layout) => layout switch
        {
            ImageLayout.ShaderReadOnlyOptimal => PipelineStageFlags.FragmentShaderBit,
            ImageLayout.TransferDstOptimal => PipelineStageFlags.TransferBit,
            _ => PipelineStageFlags.TopOfPipeBit
        };

        private PipelineStageFlags GetDstStage(ImageLayout layout) => layout switch
        {
            ImageLayout.TransferDstOptimal => PipelineStageFlags.TransferBit,
            ImageLayout.ShaderReadOnlyOptimal => PipelineStageFlags.FragmentShaderBit,
            _ => PipelineStageFlags.BottomOfPipeBit
        };


        public unsafe void TransitionImageLayout(VkCommandBuffer commandBuffer, ImageLayout newLayout)
        {
            var subresourceRange = new ImageSubresourceRange
            {
                AspectMask = newLayout == ImageLayout.DepthStencilAttachmentOptimal
                    ? ImageAspectFlags.DepthBit
                    : ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = MipLevels, // Transition all mip levels
                BaseArrayLayer = 0,
                LayerCount = 1
            };

            if (Format.HasStencilComponent() && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
            {
                subresourceRange.AspectMask |= ImageAspectFlags.StencilBit;
            }

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = _currentLayout,
                NewLayout = newLayout,
                Image = _vkObject,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                SubresourceRange = subresourceRange
            };

            // Determine pipeline stages and access masks
            (PipelineStageFlags srcStage, PipelineStageFlags dstStage) = GetPipelineStages(_currentLayout, newLayout);
            (barrier.SrcAccessMask, barrier.DstAccessMask) = GetAccessMasks(_currentLayout, newLayout);

            VulkanContext.Vk.CmdPipelineBarrier(commandBuffer,
                srcStage, dstStage,
                0, 0, null, 0, null, 1, &barrier);

            _currentLayout = newLayout;
        }
        public void TransitionImageLayout(
            VkCommandBuffer commandBuffer,
            ImageLayout oldLayout,
            ImageLayout newLayout,
            PipelineStageFlags srcStageMask,
            PipelineStageFlags dstStageMask,
            AccessFlags srcAccessMask,
            AccessFlags dstAccessMask,
            ImageAspectFlags aspectFlags = ImageAspectFlags.ColorBit)
        {
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = this,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspectFlags,
                    BaseMipLevel = 0,
                    LevelCount = MipLevels,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcAccessMask = srcAccessMask,
                DstAccessMask = dstAccessMask
            };
            unsafe
            {

                VulkanContext.Vk.CmdPipelineBarrier(
                    commandBuffer,
                    srcStageMask,
                    dstStageMask,
                    DependencyFlags.None,
                    0,
                    null,
                    0,
                    null,
                    1,
                    in barrier
                );
            }


            // Update internal layout tracking
            _currentLayout = newLayout;
        }
        public unsafe void TransitionImageLayout(
            VkCommandBuffer commandBuffer,
            ImageLayout newLayout,
            PipelineStageFlags srcStage,
            PipelineStageFlags dstStage)
        {
            var subresourceRange = new ImageSubresourceRange
            {
                AspectMask = newLayout == ImageLayout.DepthStencilAttachmentOptimal
                    ? ImageAspectFlags.DepthBit
                    : ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = MipLevels,
                BaseArrayLayer = 0,
                LayerCount = 1
            };

            if (Format.HasStencilComponent() && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
            {
                subresourceRange.AspectMask |= ImageAspectFlags.StencilBit;
            }

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = _currentLayout,
                NewLayout = newLayout,
                Image = _vkObject,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                SubresourceRange = subresourceRange
            };

            (barrier.SrcAccessMask, barrier.DstAccessMask) = GetAccessMasks(_currentLayout, newLayout);

            VulkanContext.Vk.CmdPipelineBarrier(
                commandBuffer,
                srcStage,
                dstStage,
                0,
                0, null,
                0, null,
                1, &barrier);

            _currentLayout = newLayout;
        }
        public unsafe void TransitionImageLayout(VkCommandBuffer commandBuffer, ImageLayout oldLayout, ImageLayout newLayout)
        {
            _currentLayout = oldLayout;
            TransitionImageLayout(commandBuffer, newLayout);
        }
        private (PipelineStageFlags, PipelineStageFlags) GetPipelineStages(ImageLayout oldLayout, ImageLayout newLayout)
        {
            return (oldLayout, newLayout) switch
            {
                // Existing transitions
                (ImageLayout.Undefined, ImageLayout.TransferDstOptimal) =>
                    (PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit),
                (ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit),
                (ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit),
                (ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal) =>
                    (PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit),
                (ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal) =>
                    (PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit),
                (ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                (PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.FragmentShaderBit),

                // Add depth/stencil transitions
                (ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal) =>
                    (PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.EarlyFragmentTestsBit),
                (ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal) =>
                    (PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.FragmentShaderBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ColorAttachmentOptimal) =>
                    (PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ColorAttachmentOutputBit),
                (ImageLayout.PresentSrcKhr, ImageLayout.ColorAttachmentOptimal) =>
                    (PipelineStageFlags.BottomOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit),

                _ => throw new NotSupportedException($"Unsupported layout transition: {oldLayout} -> {newLayout}")
            };
        }

        private (AccessFlags, AccessFlags) GetAccessMasks(ImageLayout oldLayout, ImageLayout newLayout)
        {
            return (oldLayout, newLayout) switch
            {
                // Existing transitions
                (ImageLayout.Undefined, ImageLayout.TransferDstOptimal) =>
                    (AccessFlags.None, AccessFlags.TransferWriteBit),
                (ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit),
                (ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags.TransferReadBit, AccessFlags.ShaderReadBit),
                (ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal) =>
                    (AccessFlags.TransferWriteBit, AccessFlags.TransferReadBit),

                (ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal) =>
                    (AccessFlags.None, AccessFlags.ColorAttachmentWriteBit),

                (ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags.ColorAttachmentWriteBit, AccessFlags.ShaderReadBit),
                // Add depth/stencil transitions
                (ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal) =>
                    (AccessFlags.None, AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit),
                (ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags.None, AccessFlags.ShaderReadBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ColorAttachmentOptimal) =>
                    (AccessFlags.ShaderReadBit, AccessFlags.ColorAttachmentWriteBit),
                (ImageLayout.PresentSrcKhr, ImageLayout.ColorAttachmentOptimal) =>
                    (AccessFlags.MemoryReadBit, AccessFlags.ColorAttachmentWriteBit),
                _ => throw new NotSupportedException($"Unsupported layout transition: {oldLayout} -> {newLayout}")
            };
        }

        public unsafe void GenerateMipmaps(VkCommandBuffer cmd, Format format)
        {
            var vk = VulkanContext.Vk;
            var mipLevels = MipLevels;

            var formatProperties = _context.Device.PhysicalDevice.GetFormatProperties(format);

            if ((formatProperties.OptimalTilingFeatures & FormatFeatureFlags.SampledImageFilterLinearBit) == 0)
                throw new NotSupportedException($"Texture format {format} doesn't support linear blitting!");

            for (uint i = 1; i < mipLevels; i++)
            {
                var barrier = new ImageMemoryBarrier()
                {
                    SType = StructureType.ImageMemoryBarrier,
                    Image = this,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = i - 1,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    OldLayout = ImageLayout.TransferDstOptimal,
                    NewLayout = ImageLayout.TransferSrcOptimal,
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.TransferReadBit
                };

                vk.CmdPipelineBarrier(cmd,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.TransferBit,
                    0,
                    0, null,
                    0, null,
                    1, &barrier);

                var blit = new ImageBlit
                {
                    SrcSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = i - 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    SrcOffsets = new ImageBlit.SrcOffsetsBuffer
                    {
                        Element0 = new Offset3D(0, 0, 0),
                        Element1 = new Offset3D((int)Width >> (int)(i - 1), (int)Height >> (int)(i - 1), 1)
                    },
                    DstSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = i,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    DstOffsets = new ImageBlit.DstOffsetsBuffer
                    {
                        Element0 = new Offset3D(0, 0, 0),
                        Element1 = new Offset3D((int)Width >> (int)i, (int)Height >> (int)i, 1)
                    }
                };

                vk.CmdBlitImage(cmd,
                    this, ImageLayout.TransferSrcOptimal,
                    this, ImageLayout.TransferDstOptimal,
                    1, &blit,
                    Filter.Linear);

                barrier.OldLayout = ImageLayout.TransferSrcOptimal;
                barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
                barrier.SrcAccessMask = AccessFlags.TransferReadBit;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;

                vk.CmdPipelineBarrier(cmd,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.FragmentShaderBit,
                    0,
                    0, null,
                    0, null,
                    1, &barrier);
            }

            var finalBarrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                Image = this,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = mipLevels - 1,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                OldLayout = ImageLayout.TransferDstOptimal,
                NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit
            };

            vk.CmdPipelineBarrier(cmd,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                0,
                0, null,
                0, null,
                1, &finalBarrier);
            for (uint i = 0; i < MipLevels; i++)
            {
                SetMipLayout(i, finalBarrier.NewLayout);
            }
        }

        private unsafe void DisposeResources()
        {
            VulkanContext.Vk.DestroyImage(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkImage>());
            _imageMemory.Dispose();
        }

        protected override unsafe void Dispose(bool disposing)
        {
            DisposeResources();
            ImageMemory.Dispose();
            _disposed = true;
        }

        public void SetCurrentLayout(ImageLayout layout)
        {
            _currentLayout = layout;
        }

       
    }
}
