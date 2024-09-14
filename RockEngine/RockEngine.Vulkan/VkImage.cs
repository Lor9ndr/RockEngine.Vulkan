using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkImage : VkObject<Image>
    {
        private readonly VkDeviceMemory _imageMemory;
        private readonly RenderingContext _context;

        public VkDeviceMemory ImageMemory => _imageMemory;
        private ImageLayout _currentLayout;

        private VkImage(RenderingContext context, Image vkImage, VkDeviceMemory imageMemory, ImageLayout currentLayout)
            : base(vkImage)

        {
            _imageMemory = imageMemory;
            _currentLayout = currentLayout;
            _context = context;
        }

        public unsafe static VkImage Create(RenderingContext context, in ImageCreateInfo ci, MemoryPropertyFlags memPropertyFlags)
        {
            RenderingContext.Vk.CreateImage(context.Device, in ci, in RenderingContext.CustomAllocator, out var vkImage)
                .VkAssertResult("Failed to create image!");
            RenderingContext.Vk.GetImageMemoryRequirements(context.Device, vkImage, out var memRequirements);

            var imageMemory = VkDeviceMemory.Allocate(context, memRequirements, memPropertyFlags);

            RenderingContext.Vk.BindImageMemory(context.Device, vkImage, imageMemory, 0);
            return new VkImage(context, vkImage, imageMemory, ci.InitialLayout);
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

        public unsafe void CopyBufferToImage(VkCommandPool commandPool, VkBuffer buffer, VkImage image, ImageLayout layout, uint width, uint height)
        {
            using var commandBuffer = VkHelper.BeginSingleTimeCommands(commandPool);

            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
                ImageExtent = new Extent3D { Width = width, Height = height, Depth = 1 }
            };

            RenderingContext.Vk.CmdCopyBufferToImage(
                commandBuffer,
                buffer,
                image,
                layout,
                1,
                &region
            );

            VkHelper.EndSingleTimeCommands(commandBuffer);
        }


        protected unsafe override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RenderingContext.Vk.DestroyImage(_context.Device, _vkObject, in RenderingContext.CustomAllocator);
                ImageMemory.Dispose();
                _disposed = true;
            }
        }
    }
}
