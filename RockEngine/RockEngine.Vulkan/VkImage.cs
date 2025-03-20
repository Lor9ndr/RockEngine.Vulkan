using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkImage : VkObject<Image>
    {
        private readonly VkDeviceMemory _imageMemory;
        private readonly RenderingContext _context;
        private readonly Format _format;

        public VkDeviceMemory ImageMemory => _imageMemory;

        public ImageLayout CurrentLayout => _currentLayout;

        public Format Format => _format;

        private ImageLayout _currentLayout;

        public VkImage(RenderingContext context, Image vkImage, VkDeviceMemory imageMemory, ImageLayout currentLayout, Format format)
            : base(vkImage)

        {
            _imageMemory = imageMemory;
            _currentLayout = currentLayout;
            _context = context;
            _format = format;
        }

        public unsafe static VkImage Create(RenderingContext context, in ImageCreateInfo ci, MemoryPropertyFlags memPropertyFlags)
        {
            RenderingContext.Vk.CreateImage(context.Device, in ci, in RenderingContext.CustomAllocator<VkImage>(), out var vkImage)
                .VkAssertResult("Failed to create image!");
            RenderingContext.Vk.GetImageMemoryRequirements(context.Device, vkImage, out var memRequirements);

            var imageMemory = VkDeviceMemory.Allocate(context, memRequirements, memPropertyFlags);

            RenderingContext.Vk.BindImageMemory(context.Device, vkImage, imageMemory, 0);
            return new VkImage(context, vkImage, imageMemory, ci.InitialLayout,ci.Format);
        }
        public static VkImage Create(
           RenderingContext context,
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

        public unsafe VkImageView CreateView(
            ImageAspectFlags aspectFlags,
            uint mipLevels = 1,
            uint arrayLayers = 1)
        {
            return VkImageView.Create(_context,this, Format, aspectFlags, mipLevels: mipLevels, arrayLayers: arrayLayers);
        }

        public unsafe void TransitionImageLayout(VkCommandBuffer commandBuffer, Format format, ImageLayout newLayout)
        {

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = _currentLayout,
                NewLayout = newLayout,
                Image = _vkObject,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            PipelineStageFlags srcStage;
            PipelineStageFlags dstStage;

            if (newLayout == ImageLayout.DepthStencilAttachmentOptimal)
            {
                barrier.SubresourceRange.AspectMask = ImageAspectFlags.DepthBit;

                if (format.HasStencilComponent())
                {
                    barrier.SubresourceRange.AspectMask |= ImageAspectFlags.StencilBit;
                }
            }
            else
            {
                barrier.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
            }


            if (_currentLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
            {
                barrier.SrcAccessMask = 0;
                barrier.DstAccessMask = AccessFlags.TransferWriteBit;

                srcStage = PipelineStageFlags.TopOfPipeBit;
                dstStage = PipelineStageFlags.TransferBit;
            }
            else if (_currentLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;

                srcStage = PipelineStageFlags.TransferBit;
                dstStage = PipelineStageFlags.FragmentShaderBit;
            }
            else if (_currentLayout == ImageLayout.Undefined && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
            {
                barrier.SrcAccessMask = 0;
                barrier.DstAccessMask = AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;

                srcStage = PipelineStageFlags.TopOfPipeBit;
                dstStage = PipelineStageFlags.EarlyFragmentTestsBit;
            }
            else if (_currentLayout == ImageLayout.Undefined && newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.ColorAttachmentWriteBit;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;
                srcStage = PipelineStageFlags.ColorAttachmentOutputBit;
                dstStage = PipelineStageFlags.FragmentShaderBit;
            }
            else
            {
                throw new Exception("Unsupported layout transition");
            }

            RenderingContext.Vk.CmdPipelineBarrier(commandBuffer,
                                                   srcStage,
                                                   dstStage,
                                                   0,
                                                   0,
                                                   null,
                                                   0,
                                                   null,
                                                   1,
                                                   &barrier);

            _currentLayout = newLayout;
        }


        protected unsafe override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RenderingContext.Vk.DestroyImage(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkImage>());
                ImageMemory.Dispose();
                _disposed = true;
            }
        }

       
    }
}
