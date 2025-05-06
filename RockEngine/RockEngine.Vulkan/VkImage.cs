using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public sealed class VkImage : VkObject<Image>
    {
        private readonly VulkanContext _context;
        private VkDeviceMemory _imageMemory;
        private ImageCreateInfo _createInfo;
        private readonly ImageAspectFlags _aspectFlags;
        private readonly ImageLayout[,] _layerMipLayouts;


        public VkDeviceMemory ImageMemory => _imageMemory;
        public Format Format => _createInfo.Format;
        public uint MipLevels => _createInfo.MipLevels;
        public Extent3D Extent => _createInfo.Extent;
        public ImageAspectFlags AspectFlags => _aspectFlags;

        public event Action<VkImage>? OnImageResized;

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
                    _layerMipLayouts[mip, layer] = ImageLayout.Undefined;
                }
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

        public void Resize(Extent3D newExtent)
        {
            DisposeResources();
            _createInfo.Extent = newExtent;

            var newImage = CreateImage(_context, _createInfo);
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
            var batch = _context.SubmitContext.CreateBatch();
            TransitionImageLayout(batch.CommandBuffer, ImageLayout.ShaderReadOnlyOptimal,0,1);
            batch.Submit();
        }

        public VkImageView CreateView(
            ImageAspectFlags aspectFlags,
            uint baseMipLevel = 0,
            uint levelCount = 1,
            uint baseArrayLayer = 0,
            uint arrayLayers = 1)
        {
            return VkImageView.Create(
                _context,
                this,
                Format,
                aspectFlags,
                baseMipLevel,
                levelCount,
                baseArrayLayer,
                arrayLayers);
        }

        public void TransitionImageLayout(
              VkCommandBuffer commandBuffer,
              ImageLayout newLayout,
              PipelineStageFlags srcStage,
              PipelineStageFlags dstStage,
              uint baseMipLevel = 0,
              uint levelCount = 1,
              uint baseArrayLayer = 0,
              uint layerCount = 1)
        {
            ImageLayout oldLayout = GetMipLayout(baseMipLevel, baseArrayLayer);

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
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
                }
            };

            (barrier.SrcAccessMask, barrier.DstAccessMask) = GetAccessMasks(oldLayout, newLayout);

            unsafe
            {
                VulkanContext.Vk.CmdPipelineBarrier(
                    commandBuffer,
                    srcStage,
                    dstStage,
                    0,
                    0, null,
                    0, null,
                    1, in barrier);
            }

            // Update tracked layouts for all affected layers/mips
            for (uint mip = baseMipLevel; mip < baseMipLevel + levelCount; mip++)
            {
                for (uint layer = baseArrayLayer; layer < baseArrayLayer + layerCount; layer++)
                {
                    SetMipLayout(mip, newLayout, layer);
                }
            }
        }
        public void TransitionImageLayout(VkCommandBuffer commandBuffer, ImageLayout newLayout, uint baseMipLevel = 0, uint levelCount = 1, uint baseArrayLayer = 0,uint layerCount = 1)
        {
            ImageLayout oldLayout = GetMipLayout(baseMipLevel, baseArrayLayer);
            (var OldStage, var NewStage) = GetPipelineStages(oldLayout, newLayout);

            TransitionImageLayout(commandBuffer, newLayout, OldStage, NewStage, baseMipLevel, levelCount, baseArrayLayer, layerCount);
        }


        public void GenerateMipmaps(VkCommandBuffer commandBuffer)
        {
            ValidateMipmapGeneration();

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
        }

        private void ValidateMipmapGeneration()
        {
            var formatProperties = _context.Device.PhysicalDevice.GetFormatProperties(Format);
            if ((formatProperties.OptimalTilingFeatures & FormatFeatureFlags.BlitSrcBit) == 0 ||
                (formatProperties.OptimalTilingFeatures & FormatFeatureFlags.BlitDstBit) == 0)
            {
                throw new NotSupportedException($"Format {Format} doesn't support blitting!");
            }
        }

        private unsafe void BlitMipLevel(VkCommandBuffer commandBuffer, in ImageBlit blit)
        {
            VulkanContext.Vk.CmdBlitImage(
                commandBuffer,
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
    VkCommandBuffer commandBuffer,
    uint mipLevel,
    ImageLayout newLayout,
    uint layer)
        {
            // Get current layout for THIS mip+layer
            ImageLayout oldLayout = GetMipLayout(mipLevel, layer);
            if (oldLayout == newLayout) return;

            var (srcAccess, dstAccess) = GetAccessMasks(oldLayout, newLayout);
            var (srcStage, dstStage) = GetPipelineStages(oldLayout, newLayout);

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
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

            unsafe
            {
                VulkanContext.Vk.CmdPipelineBarrier(
                    commandBuffer,
                    srcStage,
                    dstStage,
                    0,
                    0, null,
                    0, null,
                    1, in barrier);
            }

            // Update tracked layout for THIS mip+layer
            SetMipLayout(mipLevel, newLayout, layer);
        }

        protected override void Dispose(bool disposing)
        {
            if (IsDisposed) return;

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
            => _layerMipLayouts[mipLevel, layer];

        public void SetMipLayout(uint mipLevel, ImageLayout layout, uint layer = 0)
            => _layerMipLayouts[mipLevel, layer] = layout;


        private (PipelineStageFlags oldStage, PipelineStageFlags newStage) GetPipelineStages(ImageLayout oldLayout, ImageLayout newLayout)
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
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal) =>
                    (PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.TransferBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferSrcOptimal) =>
                    (PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.TransferBit),
                (ImageLayout.Undefined, ImageLayout.TransferSrcOptimal) =>
                    (PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit),
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

        private (AccessFlags srcAccessFlag, AccessFlags dstAccessFlag) GetAccessMasks(ImageLayout oldLayout, ImageLayout newLayout)
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
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal) =>
                    (AccessFlags.ShaderReadBit, AccessFlags.TransferWriteBit),

                (ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal) =>
                    (AccessFlags.None, AccessFlags.ColorAttachmentWriteBit),
                (ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags.ColorAttachmentWriteBit, AccessFlags.ShaderReadBit),
                (ImageLayout.Undefined, ImageLayout.TransferSrcOptimal) =>
                    (AccessFlags.None, AccessFlags.TransferReadBit),

                // Add depth/stencil transitions
                (ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal) =>
                    (AccessFlags.None, AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit),
                (ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal) =>
                    (AccessFlags.None, AccessFlags.ShaderReadBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ColorAttachmentOptimal) =>
                    (AccessFlags.ShaderReadBit, AccessFlags.ColorAttachmentWriteBit),
                (ImageLayout.PresentSrcKhr, ImageLayout.ColorAttachmentOptimal) =>
                    (AccessFlags.MemoryReadBit, AccessFlags.ColorAttachmentWriteBit),
                (ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferSrcOptimal) =>
                    (AccessFlags.ShaderReadBit, AccessFlags.TransferReadBit),

                _ => throw new NotSupportedException($"Unsupported layout transition: {oldLayout} -> {newLayout}")
            };
        }
        public override void LabelObject(string name) { }

    }
}
