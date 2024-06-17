using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace RockEngine.Vulkan.VkObjects
{
    internal class Texture : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly Silk.NET.Vulkan.Image _vkImage;
        private readonly DeviceMemory _imageMemory;
        private readonly ImageView _imageView;

        private Texture(VulkanContext context, Silk.NET.Vulkan.Image vkImage, DeviceMemory imageMemory, ImageView imageView)
        {
            _context = context;
            _vkImage = vkImage;
            _imageMemory = imageMemory;
            _imageView = imageView;
        }

        public static async Task<Texture> FromFileAsync(VulkanContext context, string path, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            using var skImage = SKImage.FromBitmap(SKBitmap.Decode(bytes));

            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;

            var format = GetVulkanFormat(skImage);
            var vkImage = CreateVulkanImage(context, width, height, format);
            var imageMemory = AllocateImageMemory(context, vkImage);
            var imageView = CreateImageView(context, vkImage, format);

            // Copy image data from SkiaSharp to Vulkan image
            CopyImageData(context, skImage, vkImage);

            return new Texture(context, vkImage, imageMemory, imageView);
        }

        private unsafe static Silk.NET.Vulkan.Image CreateVulkanImage(VulkanContext context, uint width, uint height, Format format)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D
                {
                    Width = width,
                    Height = height,
                    Depth = 1
                },
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };

            context.Api.CreateImage(context.Device, in imageInfo, null, out var vkImage)
                .ThrowCode("Failed to create image!");

            return vkImage;
        }

        private static DeviceMemory AllocateImageMemory(VulkanContext context, Silk.NET.Vulkan.Image vkImage)
        {
            context.Api.GetImageMemoryRequirements(context.Device, vkImage, out var memRequirements);

            return DeviceMemory.Allocate(context, memRequirements, MemoryPropertyFlags.DeviceLocalBit);
        }

        private unsafe static ImageView CreateImageView(VulkanContext context, Silk.NET.Vulkan.Image vkImage, Format format)
        {

            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = vkImage,
                ViewType = ImageViewType.Type2D,
                Format = format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            return ImageView.Create(context, in viewInfo);
        }
        private static CommandBufferWrapper BeginSingleTimeCommands(VulkanContext context, CommandPoolWrapper commandPool)
        {

            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = commandPool,
                CommandBufferCount = 1
            };
            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            var commandBuffer = CommandBufferWrapper.Create(context, in allocInfo, commandPool);

           commandBuffer.Begin(beginInfo);

            return commandBuffer;
        }

        private unsafe static void EndSingleTimeCommands(VulkanContext context, CommandBufferWrapper commandBuffer, CommandPoolWrapper commandPool)
        {
            var vk = context.Api;
            var device = context.Device;

            vk.EndCommandBuffer(commandBuffer).ThrowCode("Failed to end command buffer!");
            var commandBufferNative = commandBuffer.VkObjectNative;
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBufferNative
            };

            vk.QueueSubmit(context.Device.GraphicsQueue, 1, in submitInfo, default);
            vk.QueueWaitIdle(context.Device.GraphicsQueue);

            vk.FreeCommandBuffers(device, commandPool, 1, in commandBufferNative);
        }
        private static Format GetVulkanFormat(SKImage skImage)
        {
            // Determine the Vulkan format based on the SkiaSharp image color type
            return skImage.ColorType switch
            {
                SKColorType.Rgba8888 => Format.R8G8B8A8Unorm,
                SKColorType.Bgra8888 => Format.B8G8R8A8Unorm,
                _ => throw new NotSupportedException($"Unsupported color type: {skImage.ColorType}")
            };
        }

        private static void CopyImageData(VulkanContext context, SKImage skImage, Silk.NET.Vulkan.Image vkImage)
        {
            var format = GetVulkanFormat(skImage);
            var width = (uint)skImage.Width;
            var height = (uint)skImage.Height;
            var imageSize = skImage.Info.BytesSize64;

            // Create a staging buffer
            BufferCreateInfo bci = new BufferCreateInfo()
            {
                SType = StructureType.BufferCreateInfo,
                Usage = BufferUsageFlags.TransferSrcBit,
                Size = (ulong)imageSize,
                SharingMode = SharingMode.Exclusive
            };
            using var stagingBuffer = BufferWrapper.Create(context, in bci, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            // Map the buffer and copy the image data
            stagingBuffer.MapMemory(out var data);
            skImage.PeekPixels().ReadPixels(new SKImageInfo((int)width, (int)height, skImage.ColorType, SKAlphaType.Premul), data, (int)(width * 4));
            stagingBuffer.UnmapMemory();

            // Transition the Vulkan image layout to TRANSFER_DST_OPTIMAL
            TransitionImageLayout(context, vkImage, format, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

            // Copy the data from the staging buffer to the Vulkan image
            CopyBufferToImage(context, stagingBuffer, vkImage, width, height);

            // Transition the Vulkan image layout to SHADER_READ_ONLY_OPTIMAL
            TransitionImageLayout(context, vkImage, format, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

            // Clean up the staging buffer
            stagingBuffer.Dispose();
        }

        private unsafe static void TransitionImageLayout(VulkanContext context, Silk.NET.Vulkan.Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
        {
            var commandPool = context.GetOrCreateCommandPool();
            var commandBuffer = BeginSingleTimeCommands(context, commandPool);

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            var srcStage = PipelineStageFlags.TopOfPipeBit;
            var dstStage = PipelineStageFlags.TransferBit;

            if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;
                srcStage = PipelineStageFlags.TransferBit;
                dstStage = PipelineStageFlags.FragmentShaderBit;
            }

            context.Api.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);

            EndSingleTimeCommands(context, commandBuffer, commandPool);
        }

        private unsafe static void CopyBufferToImage(VulkanContext context, BufferWrapper stagingBuffer, Silk.NET.Vulkan.Image image, uint width, uint height)
        {
            var commandPool = context.GetOrCreateCommandPool();
            var commandBuffer = BeginSingleTimeCommands(context, commandPool);

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

            context.Api.CmdCopyBufferToImage(commandBuffer, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);

            EndSingleTimeCommands(context, commandBuffer, commandPool);
        }

        public unsafe void Dispose()
        {
            _imageView.Dispose();
            _context.Api.DestroyImage(_context.Device, _vkImage, null);
            _imageMemory.Dispose();
        }
    }
}