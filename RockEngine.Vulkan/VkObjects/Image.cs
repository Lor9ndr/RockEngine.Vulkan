using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class Image : VkObject<Silk.NET.Vulkan.Image>
    {
        private readonly DeviceMemory _imageMemory;
        private readonly VulkanContext _context;

        private Image(VulkanContext context, Silk.NET.Vulkan.Image vkImage, DeviceMemory imageMemory)
            : base(vkImage)

        {
            _imageMemory = imageMemory;
            _context = context;
        }

        public unsafe static Image Create(VulkanContext context, in ImageCreateInfo ci, MemoryPropertyFlags memPropertyFlags)
        {
            context.Api.CreateImage(context.Device, in ci, null, out var vkImage)
                .ThrowCode("Failed to create image!");
            context.Api.GetImageMemoryRequirements(context.Device, vkImage, out var memRequirements);

            var imageMemory = DeviceMemory.Allocate(context, memRequirements, memPropertyFlags);

            context.Api.BindImageMemory(context.Device, vkImage, imageMemory, 0);
            return new Image(context, vkImage, imageMemory);
        }

        public unsafe void TransitionImageLayout(VulkanContext context, Format format, ImageLayout oldLayout, ImageLayout newLayout)
        {
            var commandPool = context.GetOrCreateCommandPool();
            using var commandBuffer = VkHelper.BeginSingleTimeCommands(context, commandPool);

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
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

            if(newLayout == ImageLayout.DepthStencilAttachmentOptimal)
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


            if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
            {
                barrier.SrcAccessMask = 0;
                barrier.DstAccessMask = AccessFlags.TransferWriteBit;

                srcStage = PipelineStageFlags.TopOfPipeBit;
                dstStage = PipelineStageFlags.TransferBit;
            }
            else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;

                srcStage = PipelineStageFlags.TransferBit;
                dstStage = PipelineStageFlags.FragmentShaderBit;
            }
            else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
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

            context.Api.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);

            VkHelper.EndSingleTimeCommands(context, commandBuffer, commandPool);
        }



        protected unsafe override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context.Api.DestroyImage(_context.Device, _vkObject, null);
                _imageMemory.Dispose();
                _disposed = true;
            }
        }
    }
}
